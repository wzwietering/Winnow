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

        var results = await RunPartitionsAsync(partitions, execute, CreateWinnowFailureResult, cancellationToken);

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
            partitions, execute, CreateInsertFailureResult, cancellationToken);

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
            partitions, execute, CreateUpsertFailureResult, cancellationToken);

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
        CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(_maxDegreeOfParallelism);
        var tasks = partitions.Select(
            p => RunWithSemaphoreAsync(semaphore, p, execute, createFailure, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private async Task<List<(PartitionResult<TResult> Result, int Index)>> RunPartitionsWithOffsetsAsync<TResult>(
        List<(List<TEntity> Items, int Offset)> partitions,
        Func<List<TEntity>, StrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        Func<List<TEntity>, Exception, TResult> createFailure,
        CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(_maxDegreeOfParallelism);
        var tasks = partitions.Select((p, i) =>
            RunWithSemaphoreIndexedAsync(semaphore, p.Items, i, execute, createFailure, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private async Task<PartitionResult<TResult>> RunWithSemaphoreAsync<TResult>(
        SemaphoreSlim semaphore,
        List<TEntity> partition,
        Func<List<TEntity>, StrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        Func<List<TEntity>, Exception, TResult> createFailure,
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
            return await ExecutePartitionAsync(partition, execute, createFailure, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var result = await RunWithSemaphoreAsync(semaphore, partition, execute, createFailure, cancellationToken);
        return (result, index);
    }

    private async Task<PartitionResult<TResult>> ExecutePartitionAsync<TResult>(
        List<TEntity> partition,
        Func<List<TEntity>, StrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        Func<List<TEntity>, Exception, TResult> createFailure,
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
