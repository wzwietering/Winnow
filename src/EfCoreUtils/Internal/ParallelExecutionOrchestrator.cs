using System.Diagnostics;
using EfCoreUtils.Internal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EfCoreUtils.Internal;

internal class ParallelExecutionOrchestrator<TEntity, TKey> : IDisposable
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly Func<DbContext> _contextFactory;
    private readonly int _maxDegreeOfParallelism;
    private readonly ILogger? _logger;
    private readonly Lazy<(DbContext Context, EntityKeyService<TEntity, TKey> Service)> _keyService;

    private readonly RetryOptions? _retryOptions;
    private record PartitionResult<TResult>(TResult Result, int RoundTrips, int Retries);

    internal ParallelExecutionOrchestrator(
        Func<DbContext> contextFactory,
        int maxDegreeOfParallelism,
        ILogger? logger = null,
        RetryOptions? retryOptions = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _maxDegreeOfParallelism = maxDegreeOfParallelism;
        _logger = logger;
        _retryOptions = retryOptions;
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

    internal async Task<BatchResult<TKey>> ExecuteBatchAsync(
        List<TEntity> entities,
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<BatchResult<TKey>>> execute,
        CancellationToken cancellationToken)
    {
        var partitions = EntityPartitioner.Partition(entities, _maxDegreeOfParallelism);
        var stopwatch = Stopwatch.StartNew();

        var results = await RunPartitionsAsync(partitions, execute, CreateBatchFailureResult, cancellationToken);

        stopwatch.Stop();
        var totalRoundTrips = results.Sum(r => r.RoundTrips);
        var totalRetries = results.Sum(r => r.Retries);
        return BatchResultMerger.MergeBatchResults(
            results.Select(r => r.Result).ToList(), stopwatch.Elapsed, totalRoundTrips, totalRetries);
    }

    internal async Task<InsertBatchResult<TKey>> ExecuteInsertAsync(
        List<TEntity> entities,
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<InsertBatchResult<TKey>>> execute,
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
        return BatchResultMerger.MergeInsertResults(merged, stopwatch.Elapsed, totalRoundTrips, totalRetries);
    }

    internal async Task<UpsertBatchResult<TKey>> ExecuteUpsertAsync(
        List<TEntity> entities,
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<UpsertBatchResult<TKey>>> execute,
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
        return BatchResultMerger.MergeUpsertResults(merged, stopwatch.Elapsed, totalRoundTrips, totalRetries);
    }

    private async Task<List<PartitionResult<TResult>>> RunPartitionsAsync<TResult>(
        List<List<TEntity>> partitions,
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
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
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
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
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
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
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        Func<List<TEntity>, Exception, TResult> createFailure,
        CancellationToken cancellationToken)
    {
        var result = await RunWithSemaphoreAsync(semaphore, partition, execute, createFailure, cancellationToken);
        return (result, index);
    }

    private async Task<PartitionResult<TResult>> ExecutePartitionAsync<TResult>(
        List<TEntity> partition,
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        Func<List<TEntity>, Exception, TResult> createFailure,
        CancellationToken cancellationToken)
    {
        DbContext? context = null;
        try
        {
            context = _contextFactory();
            var strategyContext = new BatchStrategyContext<TEntity, TKey>(context) { Logger = _logger, RetryOptions = _retryOptions };
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

    private BatchResult<TKey> CreateBatchFailureResult(List<TEntity> entities, Exception ex)
    {
        if (ex is OperationCanceledException)
            return new BatchResult<TKey> { WasCancelled = true, SuccessfulIds = [], Failures = [] };

        return new BatchResult<TKey>
        {
            SuccessfulIds = [],
            Failures = ExtractBatchFailures(entities, ex)
        };
    }

    private List<BatchFailure<TKey>> ExtractBatchFailures(List<TEntity> entities, Exception ex)
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

    private List<BatchFailure<TKey>> CreateFailuresWithKeys(
        List<TEntity> entities, Exception ex, FailureReason reason)
    {
        var keyService = _keyService.Value.Service;
        return entities.Select(e => new BatchFailure<TKey>
        {
            EntityId = keyService.GetEntityId(e),
            ErrorMessage = ex.Message,
            Reason = reason,
            Exception = ex
        }).ToList();
    }

    private static List<BatchFailure<TKey>> CreateFailuresWithoutKeys(
        List<TEntity> entities, Exception ex, FailureReason reason, string suffix = "")
    {
        return entities.Select(_ => new BatchFailure<TKey>
        {
            ErrorMessage = ex.Message + suffix,
            Reason = reason,
            Exception = ex
        }).ToList();
    }

    private static InsertBatchResult<TKey> CreateInsertFailureResult(List<TEntity> entities, Exception ex)
    {
        if (ex is OperationCanceledException)
            return new InsertBatchResult<TKey> { WasCancelled = true, InsertedEntities = [], Failures = [] };

        var reason = FailureClassifier.Classify(ex);
        var failures = entities.Select((_, i) => new InsertBatchFailure
        {
            EntityIndex = i,
            ErrorMessage = ex.Message,
            Reason = reason,
            Exception = ex
        }).ToList();

        return new InsertBatchResult<TKey> { InsertedEntities = [], Failures = failures };
    }

    private static UpsertBatchResult<TKey> CreateUpsertFailureResult(List<TEntity> entities, Exception ex)
    {
        if (ex is OperationCanceledException)
            return new UpsertBatchResult<TKey>
            {
                WasCancelled = true, InsertedEntities = [], UpdatedEntities = [], Failures = []
            };

        var reason = FailureClassifier.Classify(ex);
        var failures = entities.Select((_, i) => new UpsertBatchFailure<TKey>
        {
            EntityIndex = i,
            ErrorMessage = ex.Message,
            Reason = reason,
            Exception = ex
        }).ToList();

        return new UpsertBatchResult<TKey> { InsertedEntities = [], UpdatedEntities = [], Failures = failures };
    }

    private static List<(InsertBatchResult<TKey> Result, int Offset)> ZipWithOffsets(
        List<(PartitionResult<InsertBatchResult<TKey>> Result, int Index)> results,
        List<(List<TEntity> Items, int Offset)> partitions)
    {
        return results
            .Select(r => (r.Result.Result, partitions[r.Index].Offset))
            .ToList();
    }

    private static List<(UpsertBatchResult<TKey> Result, int Offset)> ZipUpsertWithOffsets(
        List<(PartitionResult<UpsertBatchResult<TKey>> Result, int Index)> results,
        List<(List<TEntity> Items, int Offset)> partitions)
    {
        return results
            .Select(r => (r.Result.Result, partitions[r.Index].Offset))
            .ToList();
    }
}
