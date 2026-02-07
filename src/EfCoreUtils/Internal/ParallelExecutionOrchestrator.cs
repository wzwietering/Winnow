using System.Diagnostics;
using EfCoreUtils.Internal.Services;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Internal;

internal class ParallelExecutionOrchestrator<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly Func<DbContext> _contextFactory;
    private readonly int _maxDegreeOfParallelism;

    private record PartitionResult<TResult>(TResult Result, int RoundTrips);

    internal ParallelExecutionOrchestrator(Func<DbContext> contextFactory, int maxDegreeOfParallelism)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _maxDegreeOfParallelism = maxDegreeOfParallelism;
    }

    internal async Task<BatchResult<TKey>> ExecuteBatchAsync(
        List<TEntity> entities,
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<BatchResult<TKey>>> execute,
        CancellationToken ct)
    {
        var partitions = EntityPartitioner.Partition(entities, _maxDegreeOfParallelism);
        var stopwatch = Stopwatch.StartNew();

        var results = await RunPartitionsAsync(partitions, execute, CreateBatchFailureResult, ct);

        stopwatch.Stop();
        var totalRoundTrips = results.Sum(r => r.RoundTrips);
        return BatchResultMerger.MergeBatchResults(results.Select(r => r.Result).ToList(), stopwatch.Elapsed, totalRoundTrips);
    }

    internal async Task<InsertBatchResult<TKey>> ExecuteInsertAsync(
        List<TEntity> entities,
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<InsertBatchResult<TKey>>> execute,
        CancellationToken ct)
    {
        var partitions = EntityPartitioner.PartitionWithOffsets(entities, _maxDegreeOfParallelism);
        var stopwatch = Stopwatch.StartNew();

        var results = await RunPartitionsWithOffsetsAsync(partitions, execute, CreateInsertFailureResult, ct);

        stopwatch.Stop();
        var totalRoundTrips = results.Sum(r => r.Result.RoundTrips);
        var merged = ZipWithOffsets(results, partitions);
        return BatchResultMerger.MergeInsertResults(merged, stopwatch.Elapsed, totalRoundTrips);
    }

    internal async Task<UpsertBatchResult<TKey>> ExecuteUpsertAsync(
        List<TEntity> entities,
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<UpsertBatchResult<TKey>>> execute,
        CancellationToken ct)
    {
        var partitions = EntityPartitioner.PartitionWithOffsets(entities, _maxDegreeOfParallelism);
        var stopwatch = Stopwatch.StartNew();

        var results = await RunPartitionsWithOffsetsAsync(partitions, execute, CreateUpsertFailureResult, ct);

        stopwatch.Stop();
        var totalRoundTrips = results.Sum(r => r.Result.RoundTrips);
        var merged = ZipUpsertWithOffsets(results, partitions);
        return BatchResultMerger.MergeUpsertResults(merged, stopwatch.Elapsed, totalRoundTrips);
    }

    private async Task<List<PartitionResult<TResult>>> RunPartitionsAsync<TResult>(
        List<List<TEntity>> partitions,
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        Func<List<TEntity>, Exception, TResult> createFailure,
        CancellationToken ct)
    {
        using var semaphore = new SemaphoreSlim(_maxDegreeOfParallelism);
        var tasks = partitions.Select(p => RunWithSemaphoreAsync(semaphore, p, execute, createFailure, ct));
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private async Task<List<(PartitionResult<TResult> Result, int Index)>> RunPartitionsWithOffsetsAsync<TResult>(
        List<(List<TEntity> Items, int Offset)> partitions,
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        Func<List<TEntity>, Exception, TResult> createFailure,
        CancellationToken ct)
    {
        using var semaphore = new SemaphoreSlim(_maxDegreeOfParallelism);
        var tasks = partitions.Select((p, i) =>
            RunWithSemaphoreIndexedAsync(semaphore, p.Items, i, execute, createFailure, ct));
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private async Task<PartitionResult<TResult>> RunWithSemaphoreAsync<TResult>(
        SemaphoreSlim semaphore,
        List<TEntity> partition,
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        Func<List<TEntity>, Exception, TResult> createFailure,
        CancellationToken ct)
    {
        try
        {
            await semaphore.WaitAsync(ct);
        }
        catch (OperationCanceledException ex)
        {
            return new PartitionResult<TResult>(createFailure(partition, ex), 0);
        }

        try
        {
            return await ExecutePartitionAsync(partition, execute, createFailure, ct);
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
        CancellationToken ct)
    {
        var result = await RunWithSemaphoreAsync(semaphore, partition, execute, createFailure, ct);
        return (result, index);
    }

    private async Task<PartitionResult<TResult>> ExecutePartitionAsync<TResult>(
        List<TEntity> partition,
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        Func<List<TEntity>, Exception, TResult> createFailure,
        CancellationToken ct)
    {
        var context = _contextFactory();
        try
        {
            var strategyContext = new BatchStrategyContext<TEntity, TKey>(context);
            var result = await execute(partition, strategyContext, ct);
            return new PartitionResult<TResult>(result, strategyContext.RoundTripCounter);
        }
        catch (Exception ex) when (ex is OperationCanceledException or DbUpdateException)
        {
            return new PartitionResult<TResult>(createFailure(partition, ex), 0);
        }
        finally
        {
            await context.DisposeAsync();
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
        using var context = _contextFactory();
        var keyService = new EntityKeyService<TEntity, TKey>(context);
        var reason = FailureClassifier.Classify(ex);

        return entities.Select(e => new BatchFailure<TKey>
        {
            EntityId = keyService.GetEntityId(e),
            ErrorMessage = ex.Message,
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
