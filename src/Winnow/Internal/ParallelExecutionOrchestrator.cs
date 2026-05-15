using System.Diagnostics;
using Winnow.Internal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Winnow.Internal;

internal class ParallelExecutionOrchestrator<TEntity, TKey> : IDisposable
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly Func<DbContext> _contextFactory;
    private readonly int _maxDegreeOfParallelism;
    private readonly ILogger? _logger;
    private readonly Lazy<(DbContext Context, EntityKeyService<TEntity, TKey> Service)> _keyService;

    private readonly RetryOptions? _retryOptions;
    private readonly ResultDetail _resultDetail;
    private record PartitionResult<TResult>(TResult Result, int RoundTrips, int Retries);

    /// <summary>
    /// Recovery hook invoked when a partition's <c>execute</c> throws
    /// <see cref="WinnowValidationException"/>. Implementations re-execute the
    /// partition without the validation-failed entities and merge the per-entity
    /// failures into the resulting payload, so that
    /// <see cref="ValidationFailureBehavior.ThrowAfterBatch"/> in parallel mode
    /// matches single-context semantics: only the offending entities fail; valid
    /// siblings still reach the database.
    /// </summary>
    private delegate Task<PartitionResult<TResult>> ValidationRecoveryAsync<TResult>(
        List<TEntity> partition,
        WinnowValidationException validationException,
        Func<List<TEntity>, StrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        CancellationToken cancellationToken);

    internal ParallelExecutionOrchestrator(
        Func<DbContext> contextFactory,
        int maxDegreeOfParallelism,
        ILogger? logger = null,
        RetryOptions? retryOptions = null,
        ResultDetail resultDetail = ResultDetail.Full)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _maxDegreeOfParallelism = maxDegreeOfParallelism;
        _logger = logger;
        _retryOptions = retryOptions;
        _resultDetail = resultDetail;
        _keyService = new Lazy<(DbContext, EntityKeyService<TEntity, TKey>)>(() =>
        {
            var ctx = _contextFactory();
            return (ctx, new EntityKeyService<TEntity, TKey>(ctx));
        });
    }

    public void Dispose()
    {
        if (_keyService.IsValueCreated)
            _keyService.Value.Context?.Dispose();
    }

    internal async Task<WinnowResult<TKey>> ExecuteBatchAsync(
        List<TEntity> entities,
        Func<List<TEntity>, StrategyContext<TEntity, TKey>, CancellationToken, Task<WinnowResult<TKey>>> execute,
        CancellationToken cancellationToken)
    {
        var partitions = EntityPartitioner.Partition(entities, _maxDegreeOfParallelism);
        var stopwatch = Stopwatch.StartNew();

        var results = await RunPartitionsAsync(
            partitions, execute, CreateWinnowFailureResult, RecoverWinnowAsync, cancellationToken);

        stopwatch.Stop();
        var totalRoundTrips = results.Sum(r => r.RoundTrips);
        var totalRetries = results.Sum(r => r.Retries);
        return ResultMerger.MergeWinnowResults(
            results.Select(r => r.Result).ToList(), stopwatch.Elapsed, totalRoundTrips, totalRetries);
    }

    internal async Task<InsertResult<TKey>> ExecuteInsertAsync(
        List<TEntity> entities,
        Func<List<TEntity>, StrategyContext<TEntity, TKey>, CancellationToken, Task<InsertResult<TKey>>> execute,
        CancellationToken cancellationToken)
    {
        var partitions = EntityPartitioner.PartitionWithOffsets(entities, _maxDegreeOfParallelism);
        var stopwatch = Stopwatch.StartNew();

        var results = await RunPartitionsWithOffsetsAsync(
            partitions, execute, CreateInsertFailureResult, RecoverInsertAsync, cancellationToken);

        stopwatch.Stop();
        var totalRoundTrips = results.Sum(r => r.Result.RoundTrips);
        var totalRetries = results.Sum(r => r.Result.Retries);
        var merged = ZipWithOffsets(results, partitions);
        return ResultMerger.MergeInsertResults(merged, stopwatch.Elapsed, totalRoundTrips, totalRetries);
    }

    internal async Task<UpsertResult<TKey>> ExecuteUpsertAsync(
        List<TEntity> entities,
        Func<List<TEntity>, StrategyContext<TEntity, TKey>, CancellationToken, Task<UpsertResult<TKey>>> execute,
        CancellationToken cancellationToken)
    {
        var partitions = EntityPartitioner.PartitionWithOffsets(entities, _maxDegreeOfParallelism);
        var stopwatch = Stopwatch.StartNew();

        var results = await RunPartitionsWithOffsetsAsync(
            partitions, execute, CreateUpsertFailureResult, RecoverUpsertAsync, cancellationToken);

        stopwatch.Stop();
        var totalRoundTrips = results.Sum(r => r.Result.RoundTrips);
        var totalRetries = results.Sum(r => r.Result.Retries);
        var merged = ZipUpsertWithOffsets(results, partitions);
        return ResultMerger.MergeUpsertResults(merged, stopwatch.Elapsed, totalRoundTrips, totalRetries);
    }

    private async Task<List<PartitionResult<TResult>>> RunPartitionsAsync<TResult>(
        List<List<TEntity>> partitions,
        Func<List<TEntity>, StrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        Func<List<TEntity>, Exception, TResult> createFailure,
        ValidationRecoveryAsync<TResult> recoverValidation,
        CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(_maxDegreeOfParallelism);
        var tasks = partitions.Select(
            p => RunWithSemaphoreAsync(semaphore, p, execute, createFailure, recoverValidation, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private async Task<List<(PartitionResult<TResult> Result, int Index)>> RunPartitionsWithOffsetsAsync<TResult>(
        List<(List<TEntity> Items, int Offset)> partitions,
        Func<List<TEntity>, StrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        Func<List<TEntity>, Exception, TResult> createFailure,
        ValidationRecoveryAsync<TResult> recoverValidation,
        CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(_maxDegreeOfParallelism);
        var tasks = partitions.Select((p, i) =>
            RunWithSemaphoreIndexedAsync(
                semaphore, p.Items, i, execute, createFailure, recoverValidation, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private async Task<PartitionResult<TResult>> RunWithSemaphoreAsync<TResult>(
        SemaphoreSlim semaphore,
        List<TEntity> partition,
        Func<List<TEntity>, StrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        Func<List<TEntity>, Exception, TResult> createFailure,
        ValidationRecoveryAsync<TResult> recoverValidation,
        CancellationToken cancellationToken)
    {
        try
        {
            await semaphore.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            return new PartitionResult<TResult>(createFailure(partition, ex), 0, 0);
        }

        try
        {
            return await ExecutePartitionAsync(partition, execute, createFailure, recoverValidation, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<(PartitionResult<TResult> Result, int Index)> RunWithSemaphoreIndexedAsync<TResult>(
        SemaphoreSlim semaphore,
        List<TEntity> partition,
        int index,
        Func<List<TEntity>, StrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        Func<List<TEntity>, Exception, TResult> createFailure,
        ValidationRecoveryAsync<TResult> recoverValidation,
        CancellationToken cancellationToken)
    {
        var result = await RunWithSemaphoreAsync(
            semaphore, partition, execute, createFailure, recoverValidation, cancellationToken);
        return (result, index);
    }

    private async Task<PartitionResult<TResult>> ExecutePartitionAsync<TResult>(
        List<TEntity> partition,
        Func<List<TEntity>, StrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        Func<List<TEntity>, Exception, TResult> createFailure,
        ValidationRecoveryAsync<TResult> recoverValidation,
        CancellationToken cancellationToken)
    {
        DbContext? context = null;
        try
        {
            context = _contextFactory();
            var strategyContext = new StrategyContext<TEntity, TKey>(context) { Logger = _logger, RetryOptions = _retryOptions };
            var result = await execute(partition, strategyContext, cancellationToken);
            return new PartitionResult<TResult>(result, strategyContext.RoundTripCounter, strategyContext.RetryCounter);
        }
        catch (WinnowValidationException validationEx) when (validationEx.Failures.Count > 0)
        {
            // Surrender the failed context immediately — recovery uses a fresh one.
            if (context is not null)
            {
                await context.DisposeAsync().ConfigureAwait(false);
                context = null;
            }
            return await recoverValidation(partition, validationEx, execute, cancellationToken);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return new PartitionResult<TResult>(createFailure(partition, ex), 0, 0);
        }
        finally
        {
            if (context is not null)
                await context.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static (HashSet<int> FailedIndices, List<TEntity> Survivors) PartitionByValidation(
        List<TEntity> partition, IReadOnlyList<WinnowValidationException.EntityFailure> failures)
    {
        var failedIndices = new HashSet<int>();
        foreach (var f in failures)
            failedIndices.Add(f.EntityIndex);

        var survivors = new List<TEntity>(Math.Max(0, partition.Count - failedIndices.Count));
        for (int i = 0; i < partition.Count; i++)
        {
            if (!failedIndices.Contains(i))
                survivors.Add(partition[i]);
        }
        return (failedIndices, survivors);
    }

    private async Task<(TResult? Result, int RoundTrips, int Retries)> RerunSurvivorsAsync<TResult>(
        List<TEntity> survivors,
        Func<List<TEntity>, StrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        CancellationToken cancellationToken)
    {
        if (survivors.Count == 0) return (default, 0, 0);
        DbContext? recoverContext = null;
        try
        {
            recoverContext = _contextFactory();
            var strategyContext = new StrategyContext<TEntity, TKey>(recoverContext) { Logger = _logger, RetryOptions = _retryOptions };
            var result = await execute(survivors, strategyContext, cancellationToken);
            return (result, strategyContext.RoundTripCounter, strategyContext.RetryCounter);
        }
        finally
        {
            if (recoverContext is not null)
                await recoverContext.DisposeAsync().ConfigureAwait(false);
        }
    }

    // === Per-result-type recovery handlers =============================

    private async Task<PartitionResult<WinnowResult<TKey>>> RecoverWinnowAsync(
        List<TEntity> partition,
        WinnowValidationException validationEx,
        Func<List<TEntity>, StrategyContext<TEntity, TKey>, CancellationToken, Task<WinnowResult<TKey>>> execute,
        CancellationToken cancellationToken)
    {
        var (_, survivors) = PartitionByValidation(partition, validationEx.Failures);
        var (survivorResult, rt, retries) = await RerunSurvivorsAsync(survivors, execute, cancellationToken);

        var validationFailures = BuildWinnowValidationFailures(partition, validationEx.Failures);
        var merged = MergeWinnowWithValidationFailures(survivorResult, validationFailures);
        return new PartitionResult<WinnowResult<TKey>>(merged, rt, retries);
    }

    private async Task<PartitionResult<InsertResult<TKey>>> RecoverInsertAsync(
        List<TEntity> partition,
        WinnowValidationException validationEx,
        Func<List<TEntity>, StrategyContext<TEntity, TKey>, CancellationToken, Task<InsertResult<TKey>>> execute,
        CancellationToken cancellationToken)
    {
        var (_, survivors) = PartitionByValidation(partition, validationEx.Failures);
        var (survivorResult, rt, retries) = await RerunSurvivorsAsync(survivors, execute, cancellationToken);

        var validationFailures = BuildInsertValidationFailures(validationEx.Failures);
        var merged = MergeInsertWithValidationFailures(survivorResult, validationFailures);
        return new PartitionResult<InsertResult<TKey>>(merged, rt, retries);
    }

    private async Task<PartitionResult<UpsertResult<TKey>>> RecoverUpsertAsync(
        List<TEntity> partition,
        WinnowValidationException validationEx,
        Func<List<TEntity>, StrategyContext<TEntity, TKey>, CancellationToken, Task<UpsertResult<TKey>>> execute,
        CancellationToken cancellationToken)
    {
        var (_, survivors) = PartitionByValidation(partition, validationEx.Failures);
        var (survivorResult, rt, retries) = await RerunSurvivorsAsync(survivors, execute, cancellationToken);

        var validationFailures = BuildUpsertValidationFailures(validationEx.Failures);
        var merged = MergeUpsertWithValidationFailures(survivorResult, validationFailures);
        return new PartitionResult<UpsertResult<TKey>>(merged, rt, retries);
    }

    // === Failure builders / mergers ====================================

    private List<WinnowFailure<TKey>> BuildWinnowValidationFailures(
        List<TEntity> partition, IReadOnlyList<WinnowValidationException.EntityFailure> failures)
    {
        if (_resultDetail < ResultDetail.Minimal) return [];
        var keyService = _keyService.Value.Service;
        var list = new List<WinnowFailure<TKey>>(failures.Count);
        foreach (var f in failures)
        {
            list.Add(new WinnowFailure<TKey>
            {
                EntityId = SafeReadKey(partition[f.EntityIndex], keyService),
                ErrorMessage = f.Message,
                Reason = FailureReason.ValidationError,
                ValidationErrors = f.Errors,
            });
        }
        return list;
    }

    private static TKey SafeReadKey(TEntity? entity, EntityKeyService<TEntity, TKey> keyService)
    {
        if (entity is null) return default!;
        try { return keyService.GetEntityIdFromInstance(entity); }
        catch { return default!; }
    }

    private List<InsertFailure> BuildInsertValidationFailures(
        IReadOnlyList<WinnowValidationException.EntityFailure> failures)
    {
        if (_resultDetail < ResultDetail.Minimal) return [];
        var list = new List<InsertFailure>(failures.Count);
        foreach (var f in failures)
        {
            list.Add(new InsertFailure
            {
                EntityIndex = f.EntityIndex,
                ErrorMessage = f.Message,
                Reason = FailureReason.ValidationError,
                ValidationErrors = f.Errors,
            });
        }
        return list;
    }

    private List<UpsertFailure<TKey>> BuildUpsertValidationFailures(
        IReadOnlyList<WinnowValidationException.EntityFailure> failures)
    {
        if (_resultDetail < ResultDetail.Minimal) return [];
        var list = new List<UpsertFailure<TKey>>(failures.Count);
        foreach (var f in failures)
        {
            list.Add(new UpsertFailure<TKey>
            {
                EntityIndex = f.EntityIndex,
                ErrorMessage = f.Message,
                Reason = FailureReason.ValidationError,
                AttemptedOperation = UpsertOperationType.Insert,
                ValidationErrors = f.Errors,
            });
        }
        return list;
    }

    private WinnowResult<TKey> MergeWinnowWithValidationFailures(
        WinnowResult<TKey>? survivor, List<WinnowFailure<TKey>> validationFailures)
    {
        if (survivor is null)
        {
            return new WinnowResult<TKey>
            {
                ResultDetail = _resultDetail,
                SuccessfulIds = [],
                Failures = validationFailures,
                SuccessCount = 0,
                FailureCount = validationFailures.Count,
            };
        }
        return new WinnowResult<TKey>
        {
            ResultDetail = survivor.ResultDetail,
            SuccessfulIds = survivor.SuccessfulIdsRaw,
            Failures = [.. survivor.FailuresRaw, .. validationFailures],
            SuccessCount = survivor.SuccessCount,
            FailureCount = survivor.FailureCount + validationFailures.Count,
            Duration = survivor.Duration,
            DatabaseRoundTrips = survivor.DatabaseRoundTrips,
            WasCancelled = survivor.WasCancelled,
            TotalRetries = survivor.TotalRetries,
            GraphHierarchy = survivor.GraphHierarchyRaw,
            TraversalInfo = survivor.TraversalInfoRaw,
        };
    }

    private InsertResult<TKey> MergeInsertWithValidationFailures(
        InsertResult<TKey>? survivor, List<InsertFailure> validationFailures)
    {
        if (survivor is null)
        {
            return new InsertResult<TKey>
            {
                ResultDetail = _resultDetail,
                InsertedEntities = [],
                Failures = validationFailures,
                SuccessCount = 0,
                FailureCount = validationFailures.Count,
            };
        }
        return new InsertResult<TKey>
        {
            ResultDetail = survivor.ResultDetail,
            InsertedEntities = survivor.InsertedEntitiesRaw,
            InsertedIds = survivor.InsertedIdsRaw,
            Failures = [.. survivor.FailuresRaw, .. validationFailures],
            SuccessCount = survivor.SuccessCount,
            FailureCount = survivor.FailureCount + validationFailures.Count,
            Duration = survivor.Duration,
            DatabaseRoundTrips = survivor.DatabaseRoundTrips,
            WasCancelled = survivor.WasCancelled,
            TotalRetries = survivor.TotalRetries,
            GraphHierarchy = survivor.GraphHierarchyRaw,
            TraversalInfo = survivor.TraversalInfoRaw,
        };
    }

    private UpsertResult<TKey> MergeUpsertWithValidationFailures(
        UpsertResult<TKey>? survivor, List<UpsertFailure<TKey>> validationFailures)
    {
        if (survivor is null)
        {
            return new UpsertResult<TKey>
            {
                ResultDetail = _resultDetail,
                InsertedEntities = [],
                UpdatedEntities = [],
                Failures = validationFailures,
                SuccessCount = 0,
                FailureCount = validationFailures.Count,
                InsertedCount = 0,
                UpdatedCount = 0,
            };
        }
        return new UpsertResult<TKey>
        {
            ResultDetail = survivor.ResultDetail,
            InsertedEntities = survivor.InsertedEntitiesRaw,
            UpdatedEntities = survivor.UpdatedEntitiesRaw,
            InsertedIds = survivor.InsertedIdsRaw,
            UpdatedIds = survivor.UpdatedIdsRaw,
            Failures = [.. survivor.FailuresRaw, .. validationFailures],
            SuccessCount = survivor.SuccessCount,
            FailureCount = survivor.FailureCount + validationFailures.Count,
            InsertedCount = survivor.InsertedCount,
            UpdatedCount = survivor.UpdatedCount,
            InsertedWithNullMatchKeyCount = survivor.InsertedWithNullMatchKeyCount,
            Duration = survivor.Duration,
            DatabaseRoundTrips = survivor.DatabaseRoundTrips,
            WasCancelled = survivor.WasCancelled,
            TotalRetries = survivor.TotalRetries,
            GraphHierarchy = survivor.GraphHierarchyRaw,
            TraversalInfo = survivor.TraversalInfoRaw,
        };
    }

    // === Pre-existing total-failure builders (for non-validation exceptions) =====

    private WinnowResult<TKey> CreateWinnowFailureResult(List<TEntity> entities, Exception ex)
    {
        if (ex is OperationCanceledException)
            return new WinnowResult<TKey>
            {
                ResultDetail = _resultDetail,
                WasCancelled = true,
                SuccessfulIds = [],
                Failures = [],
                SuccessCount = 0,
                FailureCount = 0
            };

        var failures = _resultDetail >= ResultDetail.Minimal
            ? ExtractWinnowFailures(entities, ex)
            : [];
        return new WinnowResult<TKey>
        {
            ResultDetail = _resultDetail,
            SuccessfulIds = [],
            Failures = failures,
            SuccessCount = 0,
            FailureCount = entities.Count
        };
    }

    private List<WinnowFailure<TKey>> ExtractWinnowFailures(List<TEntity> entities, Exception ex)
    {
        var reason = FailureClassifier.Classify(ex);

        try
        {
            return CreateFailuresWithKeys(entities, ex, reason);
        }
        catch (Exception keyEx)
        {
            return CreateFailuresWithoutKeys(entities, ex, reason,
                $" (Key extraction also failed: {keyEx.Message})");
        }
    }

    private List<WinnowFailure<TKey>> CreateFailuresWithKeys(
        List<TEntity> entities, Exception ex, FailureReason reason)
    {
        var keyService = _keyService.Value.Service;
        var capturedException = _resultDetail >= ResultDetail.Full ? ex : null;
        return entities.Select(e => new WinnowFailure<TKey>
        {
            EntityId = keyService.GetEntityId(e),
            ErrorMessage = ex.Message,
            Reason = reason,
            Exception = capturedException
        }).ToList();
    }

    private List<WinnowFailure<TKey>> CreateFailuresWithoutKeys(
        List<TEntity> entities, Exception ex, FailureReason reason, string suffix = "")
    {
        var capturedException = _resultDetail >= ResultDetail.Full ? ex : null;
        return entities.Select(_ => new WinnowFailure<TKey>
        {
            ErrorMessage = ex.Message + suffix,
            Reason = reason,
            Exception = capturedException
        }).ToList();
    }

    private InsertResult<TKey> CreateInsertFailureResult(List<TEntity> entities, Exception ex)
    {
        if (ex is OperationCanceledException)
            return new InsertResult<TKey>
            {
                ResultDetail = _resultDetail,
                WasCancelled = true,
                InsertedEntities = [],
                Failures = [],
                SuccessCount = 0,
                FailureCount = 0
            };

        var failures = _resultDetail >= ResultDetail.Minimal
            ? BuildInsertFailures(entities, ex)
            : [];
        return new InsertResult<TKey>
        {
            ResultDetail = _resultDetail,
            InsertedEntities = [],
            Failures = failures,
            SuccessCount = 0,
            FailureCount = entities.Count
        };
    }

    private List<InsertFailure> BuildInsertFailures(List<TEntity> entities, Exception ex)
    {
        var reason = FailureClassifier.Classify(ex);
        var capturedException = _resultDetail >= ResultDetail.Full ? ex : null;
        return entities.Select((_, i) => new InsertFailure
        {
            EntityIndex = i,
            ErrorMessage = ex.Message,
            Reason = reason,
            Exception = capturedException
        }).ToList();
    }

    private UpsertResult<TKey> CreateUpsertFailureResult(List<TEntity> entities, Exception ex)
    {
        if (ex is OperationCanceledException)
            return new UpsertResult<TKey>
            {
                ResultDetail = _resultDetail,
                WasCancelled = true,
                InsertedEntities = [],
                UpdatedEntities = [],
                Failures = [],
                SuccessCount = 0,
                FailureCount = 0,
                InsertedCount = 0,
                UpdatedCount = 0
            };

        var failures = _resultDetail >= ResultDetail.Minimal
            ? BuildUpsertFailures(entities, ex)
            : [];
        return new UpsertResult<TKey>
        {
            ResultDetail = _resultDetail,
            InsertedEntities = [],
            UpdatedEntities = [],
            Failures = failures,
            SuccessCount = 0,
            FailureCount = entities.Count,
            InsertedCount = 0,
            UpdatedCount = 0
        };
    }

    private List<UpsertFailure<TKey>> BuildUpsertFailures(List<TEntity> entities, Exception ex)
    {
        var reason = FailureClassifier.Classify(ex);
        var capturedException = _resultDetail >= ResultDetail.Full ? ex : null;
        return entities.Select((_, i) => new UpsertFailure<TKey>
        {
            EntityIndex = i,
            ErrorMessage = ex.Message,
            Reason = reason,
            Exception = capturedException
        }).ToList();
    }

    private static List<(InsertResult<TKey> Result, int Offset)> ZipWithOffsets(
        List<(PartitionResult<InsertResult<TKey>> Result, int Index)> results,
        List<(List<TEntity> Items, int Offset)> partitions)
    {
        return results
            .Select(r => (r.Result.Result, partitions[r.Index].Offset))
            .ToList();
    }

    private static List<(UpsertResult<TKey> Result, int Offset)> ZipUpsertWithOffsets(
        List<(PartitionResult<UpsertResult<TKey>> Result, int Index)> results,
        List<(List<TEntity> Items, int Offset)> partitions)
    {
        return results
            .Select(r => (r.Result.Result, partitions[r.Index].Offset))
            .ToList();
    }
}
