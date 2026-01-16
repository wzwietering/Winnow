using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils;

public class BatchSaver<TEntity, TKey>(DbContext context) : IBatchSaver<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
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
    public BatchResult<TKey> UpdateBatch(IEnumerable<TEntity> entities)
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
    public BatchResult<TKey> UpdateBatch(IEnumerable<TEntity> entities, BatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyResult(stopwatch);
        }

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context);
        var strategy = BatchStrategyFactory.CreateStrategy<TEntity, TKey>(options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichResultWithMetrics(result, stopwatch, strategyContext);
    }

    public Task<BatchResult<TKey>> UpdateBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(UpdateBatch(entities));
    }

    public Task<BatchResult<TKey>> UpdateBatchAsync(IEnumerable<TEntity> entities, BatchOptions options, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(UpdateBatch(entities, options));
    }

    /// <summary>
    /// Updates a batch of entity graphs (parent + children) using the default options.
    /// Each graph succeeds or fails as a unit - if any entity in a graph fails, the entire graph rolls back.
    /// </summary>
    /// <param name="entities">The parent entities with their navigation properties loaded</param>
    /// <returns>Result containing successful IDs, failures, child IDs by parent, and performance metrics</returns>
    public BatchResult<TKey> UpdateGraphBatch(IEnumerable<TEntity> entities)
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
    public BatchResult<TKey> UpdateGraphBatch(IEnumerable<TEntity> entities, GraphBatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyGraphResult(stopwatch);
        }

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context);
        var strategy = BatchStrategyFactory.CreateGraphStrategy<TEntity, TKey>(options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichGraphResultWithMetrics(result, stopwatch, strategyContext);
    }

    public Task<BatchResult<TKey>> UpdateGraphBatchAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(UpdateGraphBatch(entities));
    }

    public Task<BatchResult<TKey>> UpdateGraphBatchAsync(
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
    public InsertBatchResult<TKey> InsertBatch(IEnumerable<TEntity> entities)
    {
        return InsertBatch(entities, new InsertBatchOptions());
    }

    /// <summary>
    /// Inserts a batch of entities using the specified strategy and options.
    /// </summary>
    public InsertBatchResult<TKey> InsertBatch(IEnumerable<TEntity> entities, InsertBatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyInsertResult(stopwatch);
        }

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context);
        var strategy = BatchStrategyFactory.CreateInsertStrategy<TEntity, TKey>(options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichInsertResultWithMetrics(result, stopwatch, strategyContext);
    }

    public Task<InsertBatchResult<TKey>> InsertBatchAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(InsertBatch(entities));
    }

    public Task<InsertBatchResult<TKey>> InsertBatchAsync(
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
    public InsertBatchResult<TKey> InsertGraphBatch(IEnumerable<TEntity> entities)
    {
        return InsertGraphBatch(entities, new InsertGraphBatchOptions());
    }

    /// <summary>
    /// Inserts a batch of entity graphs (parent + children) using the specified options.
    /// Each graph succeeds or fails as a unit.
    /// </summary>
    public InsertBatchResult<TKey> InsertGraphBatch(IEnumerable<TEntity> entities, InsertGraphBatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyInsertGraphResult(stopwatch);
        }

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context);
        var strategy = BatchStrategyFactory.CreateInsertGraphStrategy<TEntity, TKey>(options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichInsertGraphResultWithMetrics(result, stopwatch, strategyContext);
    }

    public Task<InsertBatchResult<TKey>> InsertGraphBatchAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(InsertGraphBatch(entities));
    }

    public Task<InsertBatchResult<TKey>> InsertGraphBatchAsync(
        IEnumerable<TEntity> entities,
        InsertGraphBatchOptions options,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(InsertGraphBatch(entities, options));
    }

    // === DELETE OPERATIONS ===

    /// <summary>
    /// Deletes a batch of entities using the default strategy (OneByOne).
    /// </summary>
    public BatchResult<TKey> DeleteBatch(IEnumerable<TEntity> entities)
    {
        return DeleteBatch(entities, new DeleteBatchOptions());
    }

    /// <summary>
    /// Deletes a batch of entities using the specified strategy and options.
    /// </summary>
    public BatchResult<TKey> DeleteBatch(IEnumerable<TEntity> entities, DeleteBatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyResult(stopwatch);
        }

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context);
        var strategy = BatchStrategyFactory.CreateDeleteStrategy<TEntity, TKey>(options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichResultWithMetrics(result, stopwatch, strategyContext);
    }

    public Task<BatchResult<TKey>> DeleteBatchAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(DeleteBatch(entities));
    }

    public Task<BatchResult<TKey>> DeleteBatchAsync(
        IEnumerable<TEntity> entities,
        DeleteBatchOptions options,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(DeleteBatch(entities, options));
    }

    /// <summary>
    /// Deletes a batch of entity graphs (parent + children) using the default options.
    /// Each graph succeeds or fails as a unit.
    /// </summary>
    public BatchResult<TKey> DeleteGraphBatch(IEnumerable<TEntity> entities)
    {
        return DeleteGraphBatch(entities, new DeleteGraphBatchOptions());
    }

    /// <summary>
    /// Deletes a batch of entity graphs (parent + children) using the specified options.
    /// Each graph succeeds or fails as a unit.
    /// </summary>
    public BatchResult<TKey> DeleteGraphBatch(IEnumerable<TEntity> entities, DeleteGraphBatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyGraphResult(stopwatch);
        }

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context);
        var strategy = BatchStrategyFactory.CreateDeleteGraphStrategy<TEntity, TKey>(options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichGraphResultWithMetrics(result, stopwatch, strategyContext);
    }

    public Task<BatchResult<TKey>> DeleteGraphBatchAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(DeleteGraphBatch(entities));
    }

    public Task<BatchResult<TKey>> DeleteGraphBatchAsync(
        IEnumerable<TEntity> entities,
        DeleteGraphBatchOptions options,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(DeleteGraphBatch(entities, options));
    }

    // === PRIVATE HELPERS ===

    private BatchResult<TKey> CreateEmptyResult(Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return new BatchResult<TKey>
        {
            SuccessfulIds = [],
            Failures = [],
            Duration = stopwatch.Elapsed,
            DatabaseRoundTrips = 0
        };
    }

    private BatchResult<TKey> EnrichResultWithMetrics(
        BatchResult<TKey> result,
        Stopwatch stopwatch,
        BatchStrategyContext<TEntity, TKey> context)
    {
        return new BatchResult<TKey>
        {
            SuccessfulIds = result.SuccessfulIds,
            Failures = result.Failures,
            Duration = stopwatch.Elapsed,
            DatabaseRoundTrips = context.RoundTripCounter
        };
    }

    private BatchResult<TKey> CreateEmptyGraphResult(Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return new BatchResult<TKey>
        {
            SuccessfulIds = [],
            Failures = [],
            Duration = stopwatch.Elapsed,
            DatabaseRoundTrips = 0,
            GraphHierarchy = [],
            TraversalInfo = new GraphTraversalResult<TKey>
            {
                MaxDepthReached = 0,
                TotalEntitiesTraversed = 0,
                EntitiesByDepth = new Dictionary<int, int>()
            }
        };
    }

    private BatchResult<TKey> EnrichGraphResultWithMetrics(
        BatchResult<TKey> result,
        Stopwatch stopwatch,
        BatchStrategyContext<TEntity, TKey> context)
    {
        return new BatchResult<TKey>
        {
            SuccessfulIds = result.SuccessfulIds,
            Failures = result.Failures,
            Duration = stopwatch.Elapsed,
            DatabaseRoundTrips = context.RoundTripCounter,
            GraphHierarchy = result.GraphHierarchy,
            TraversalInfo = result.TraversalInfo
        };
    }

    private InsertBatchResult<TKey> CreateEmptyInsertResult(Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return new InsertBatchResult<TKey>
        {
            InsertedEntities = [],
            Failures = [],
            Duration = stopwatch.Elapsed,
            DatabaseRoundTrips = 0
        };
    }

    private InsertBatchResult<TKey> EnrichInsertResultWithMetrics(
        InsertBatchResult<TKey> result,
        Stopwatch stopwatch,
        BatchStrategyContext<TEntity, TKey> context)
    {
        return new InsertBatchResult<TKey>
        {
            InsertedEntities = result.InsertedEntities,
            Failures = result.Failures,
            Duration = stopwatch.Elapsed,
            DatabaseRoundTrips = context.RoundTripCounter
        };
    }

    private InsertBatchResult<TKey> CreateEmptyInsertGraphResult(Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return new InsertBatchResult<TKey>
        {
            InsertedEntities = [],
            Failures = [],
            Duration = stopwatch.Elapsed,
            DatabaseRoundTrips = 0,
            GraphHierarchy = [],
            TraversalInfo = new GraphTraversalResult<TKey>
            {
                MaxDepthReached = 0,
                TotalEntitiesTraversed = 0,
                EntitiesByDepth = new Dictionary<int, int>()
            }
        };
    }

    private InsertBatchResult<TKey> EnrichInsertGraphResultWithMetrics(
        InsertBatchResult<TKey> result,
        Stopwatch stopwatch,
        BatchStrategyContext<TEntity, TKey> context)
    {
        return new InsertBatchResult<TKey>
        {
            InsertedEntities = result.InsertedEntities,
            Failures = result.Failures,
            Duration = stopwatch.Elapsed,
            DatabaseRoundTrips = context.RoundTripCounter,
            GraphHierarchy = result.GraphHierarchy,
            TraversalInfo = result.TraversalInfo
        };
    }
}
