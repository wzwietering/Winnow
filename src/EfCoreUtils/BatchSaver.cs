using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils;

public class BatchSaver<TEntity>(DbContext context) : IBatchSaver<TEntity> where TEntity : class
{
    private readonly DbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    public BatchResult UpdateBatch(IEnumerable<TEntity> entities)
    {
        return UpdateBatch(entities, new BatchOptions());
    }

    public BatchResult UpdateBatch(IEnumerable<TEntity> entities, BatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyResult(stopwatch);
        }

        var strategyContext = new BatchStrategyContext<TEntity>(_context);
        var strategy = BatchStrategyFactory.CreateStrategy<TEntity>(options.Strategy);
        var result = strategy.Execute(entityList, strategyContext);

        stopwatch.Stop();

        return EnrichResultWithMetrics(result, stopwatch, strategyContext);
    }

    public Task<BatchResult> UpdateBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(UpdateBatch(entities));
    }

    public Task<BatchResult> UpdateBatchAsync(IEnumerable<TEntity> entities, BatchOptions options, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(UpdateBatch(entities, options));
    }

    private BatchResult CreateEmptyResult(Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return new BatchResult
        {
            SuccessfulIds = Array.Empty<int>(),
            Failures = Array.Empty<BatchFailure>(),
            Duration = stopwatch.Elapsed,
            DatabaseRoundTrips = 0
        };
    }

    private BatchResult EnrichResultWithMetrics(
        BatchResult result,
        Stopwatch stopwatch,
        BatchStrategyContext<TEntity> context)
    {
        return new BatchResult
        {
            SuccessfulIds = result.SuccessfulIds,
            Failures = result.Failures,
            Duration = stopwatch.Elapsed,
            DatabaseRoundTrips = context.RoundTripCounter
        };
    }
}
