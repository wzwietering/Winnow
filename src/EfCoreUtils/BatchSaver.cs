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

    /// <summary>
    /// Updates a batch of entity graphs (parent + children) using the default options.
    /// Each graph succeeds or fails as a unit - if any entity in a graph fails, the entire graph rolls back.
    /// </summary>
    /// <param name="entities">The parent entities with their navigation properties loaded</param>
    /// <returns>Result containing successful IDs, failures, child IDs by parent, and performance metrics</returns>
    public BatchResult UpdateGraphBatch(IEnumerable<TEntity> entities)
    {
        return UpdateGraphBatch(entities, new GraphBatchOptions());
    }

    /// <summary>
    /// Updates a batch of entity graphs (parent + children) using the specified options.
    /// Each graph succeeds or fails as a unit - if any entity in a graph fails, the entire graph rolls back.
    /// </summary>
    /// <param name="entities">The parent entities with their navigation properties loaded</param>
    /// <param name="options">Graph batch operation options</param>
    /// <returns>Result containing successful IDs, failures, child IDs by parent, and performance metrics</returns>
    public BatchResult UpdateGraphBatch(IEnumerable<TEntity> entities, GraphBatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyGraphResult(stopwatch);
        }

        var strategyContext = new BatchStrategyContext<TEntity>(_context);
        var strategy = BatchStrategyFactory.CreateGraphStrategy<TEntity>(options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichGraphResultWithMetrics(result, stopwatch, strategyContext);
    }

    public Task<BatchResult> UpdateGraphBatchAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(UpdateGraphBatch(entities));
    }

    public Task<BatchResult> UpdateGraphBatchAsync(
        IEnumerable<TEntity> entities,
        GraphBatchOptions options,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(UpdateGraphBatch(entities, options));
    }

    // === INSERT OPERATIONS ===

    /// <summary>
    /// Inserts a batch of entities using the default strategy (OneByOne).
    /// </summary>
    public InsertBatchResult InsertBatch(IEnumerable<TEntity> entities)
    {
        return InsertBatch(entities, new InsertBatchOptions());
    }

    /// <summary>
    /// Inserts a batch of entities using the specified strategy and options.
    /// </summary>
    public InsertBatchResult InsertBatch(IEnumerable<TEntity> entities, InsertBatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyInsertResult(stopwatch);
        }

        var strategyContext = new BatchStrategyContext<TEntity>(_context);
        var strategy = BatchStrategyFactory.CreateInsertStrategy<TEntity>(options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichInsertResultWithMetrics(result, stopwatch, strategyContext);
    }

    public Task<InsertBatchResult> InsertBatchAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(InsertBatch(entities));
    }

    public Task<InsertBatchResult> InsertBatchAsync(
        IEnumerable<TEntity> entities,
        InsertBatchOptions options,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(InsertBatch(entities, options));
    }

    /// <summary>
    /// Inserts a batch of entity graphs (parent + children) using the default options.
    /// Each graph succeeds or fails as a unit.
    /// </summary>
    public InsertBatchResult InsertGraphBatch(IEnumerable<TEntity> entities)
    {
        return InsertGraphBatch(entities, new InsertGraphBatchOptions());
    }

    /// <summary>
    /// Inserts a batch of entity graphs (parent + children) using the specified options.
    /// Each graph succeeds or fails as a unit.
    /// </summary>
    public InsertBatchResult InsertGraphBatch(IEnumerable<TEntity> entities, InsertGraphBatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyInsertGraphResult(stopwatch);
        }

        var strategyContext = new BatchStrategyContext<TEntity>(_context);
        var strategy = BatchStrategyFactory.CreateInsertGraphStrategy<TEntity>(options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichInsertGraphResultWithMetrics(result, stopwatch, strategyContext);
    }

    public Task<InsertBatchResult> InsertGraphBatchAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(InsertGraphBatch(entities));
    }

    public Task<InsertBatchResult> InsertGraphBatchAsync(
        IEnumerable<TEntity> entities,
        InsertGraphBatchOptions options,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(InsertGraphBatch(entities, options));
    }

    // === PRIVATE HELPERS ===

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

    private BatchResult CreateEmptyGraphResult(Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return new BatchResult
        {
            SuccessfulIds = [],
            Failures = [],
            Duration = stopwatch.Elapsed,
            DatabaseRoundTrips = 0,
            ChildIdsByParentId = new Dictionary<int, IReadOnlyList<int>>()
        };
    }

    private BatchResult EnrichGraphResultWithMetrics(
        BatchResult result,
        Stopwatch stopwatch,
        BatchStrategyContext<TEntity> context)
    {
        return new BatchResult
        {
            SuccessfulIds = result.SuccessfulIds,
            Failures = result.Failures,
            Duration = stopwatch.Elapsed,
            DatabaseRoundTrips = context.RoundTripCounter,
            ChildIdsByParentId = result.ChildIdsByParentId
        };
    }

    private InsertBatchResult CreateEmptyInsertResult(Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return new InsertBatchResult
        {
            InsertedEntities = [],
            Failures = [],
            Duration = stopwatch.Elapsed,
            DatabaseRoundTrips = 0
        };
    }

    private InsertBatchResult EnrichInsertResultWithMetrics(
        InsertBatchResult result,
        Stopwatch stopwatch,
        BatchStrategyContext<TEntity> context)
    {
        return new InsertBatchResult
        {
            InsertedEntities = result.InsertedEntities,
            Failures = result.Failures,
            Duration = stopwatch.Elapsed,
            DatabaseRoundTrips = context.RoundTripCounter
        };
    }

    private InsertBatchResult CreateEmptyInsertGraphResult(Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return new InsertBatchResult
        {
            InsertedEntities = [],
            Failures = [],
            Duration = stopwatch.Elapsed,
            DatabaseRoundTrips = 0,
            ChildIdsByParentId = new Dictionary<int, IReadOnlyList<int>>()
        };
    }

    private InsertBatchResult EnrichInsertGraphResultWithMetrics(
        InsertBatchResult result,
        Stopwatch stopwatch,
        BatchStrategyContext<TEntity> context)
    {
        return new InsertBatchResult
        {
            InsertedEntities = result.InsertedEntities,
            Failures = result.Failures,
            Duration = stopwatch.Elapsed,
            DatabaseRoundTrips = context.RoundTripCounter,
            ChildIdsByParentId = result.ChildIdsByParentId
        };
    }
}
