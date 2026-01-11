using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils;

public class BatchSaver<TEntity>(DbContext context) : IBatchSaver<TEntity> where TEntity : class
{
    private readonly DbContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>
    /// Updates a batch of entities using the default strategy (OneByOne).
    /// Only properties of TEntity are updated. Navigation properties are NOT updated
    /// even if loaded with .Include(). For entity graph updates, use standard EF Core SaveChanges().
    /// </summary>
    /// <param name="entities">The entities to update</param>
    /// <returns>Result containing successful IDs, failures, and performance metrics</returns>
    /// <exception cref="InvalidOperationException">Thrown when navigation properties are modified and ValidateNavigationProperties is true</exception>
    public BatchResult UpdateBatch(IEnumerable<TEntity> entities)
    {
        return UpdateBatch(entities, new BatchOptions());
    }

    /// <summary>
    /// Updates a batch of entities using the specified strategy and options.
    /// Only properties of TEntity are updated. Navigation properties are NOT updated
    /// even if loaded with .Include(). For entity graph updates, use standard EF Core SaveChanges().
    /// </summary>
    /// <param name="entities">The entities to update</param>
    /// <param name="options">Batch operation options</param>
    /// <returns>Result containing successful IDs, failures, and performance metrics</returns>
    /// <exception cref="InvalidOperationException">Thrown when navigation properties are modified and ValidateNavigationProperties is true</exception>
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
        var result = strategy.Execute(entityList, strategyContext, options);

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
            SuccessfulIds = [],
            Failures = [],
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
