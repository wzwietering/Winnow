using System.Diagnostics;
using EfCoreUtils.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EfCoreUtils;

/// <summary>
/// Batch saver that processes entities with failure isolation.
/// </summary>
public class BatchSaver<TEntity, TKey> : IBatchSaver<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly DbContext _context;
    private readonly ILogger? _logger;

    /// <summary>
    /// Creates a BatchSaver without logging.
    /// </summary>
    public BatchSaver(DbContext context) : this(context, (ILogger?)null) { }

    /// <summary>
    /// Creates a BatchSaver with typed logger support (used by DI).
    /// </summary>
    public BatchSaver(DbContext context, ILogger<BatchSaver<TEntity, TKey>>? logger)
        : this(context, (ILogger?)logger) { }

    /// <summary>
    /// Creates a BatchSaver with optional logger support.
    /// </summary>
    public BatchSaver(DbContext context, ILogger? logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger;
    }

    /// <summary>
    /// Updates a batch of entities using the default strategy (OneByOne).
    /// Only properties of TEntity are updated. Navigation properties are NOT updated
    /// even if loaded with .Include(). For entity graph updates, use standard EF Core SaveChanges().
    /// </summary>
    /// <param name="entities">The entities to update</param>
    /// <returns>Result containing successful IDs, failures, and performance metrics</returns>
    /// <exception cref="InvalidOperationException">Thrown when navigation properties are modified and ValidateNavigationProperties is true</exception>
    public BatchResult<TKey> UpdateBatch(IEnumerable<TEntity> entities) => UpdateBatch(entities, new BatchOptions());

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

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context) { Logger = _logger, RetryOptions = options.Retry };
        var strategy = BatchStrategyFactory.CreateStrategy<TEntity, TKey>(options.Strategy);
        BatchLogger.LogBatchStarting(_logger, "UpdateBatch", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichResultWithMetrics(result, stopwatch, strategyContext, "UpdateBatch");
    }

    public Task<BatchResult<TKey>> UpdateBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        UpdateBatchAsync(entities, new BatchOptions(), cancellationToken);

    public async Task<BatchResult<TKey>> UpdateBatchAsync(IEnumerable<TEntity> entities, BatchOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyResult(stopwatch);
        }

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context) { Logger = _logger, RetryOptions = options.Retry };
        var strategy = BatchStrategyFactory.CreateStrategy<TEntity, TKey>(options.Strategy);
        BatchLogger.LogBatchStarting(_logger, "UpdateBatch", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = await strategy.ExecuteAsync(entityList, strategyContext, options, cancellationToken);

        stopwatch.Stop();

        return EnrichResultWithMetrics(result, stopwatch, strategyContext, "UpdateBatch");
    }

    /// <summary>
    /// Updates a batch of entity graphs (parent + children) using the default options.
    /// Each graph succeeds or fails as a unit - if any entity in a graph fails, the entire graph rolls back.
    /// </summary>
    /// <param name="entities">The parent entities with their navigation properties loaded</param>
    /// <returns>Result containing successful IDs, failures, child IDs by parent, and performance metrics</returns>
    public BatchResult<TKey> UpdateGraphBatch(IEnumerable<TEntity> entities) => UpdateGraphBatch(entities, new GraphBatchOptions());

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

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context) { Logger = _logger, RetryOptions = options.Retry };
        var strategy = BatchStrategyFactory.CreateGraphStrategy<TEntity, TKey>(options.Strategy);
        BatchLogger.LogBatchStarting(_logger, "UpdateGraphBatch", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichResultWithMetrics(result, stopwatch, strategyContext, "UpdateGraphBatch");
    }

    public Task<BatchResult<TKey>> UpdateGraphBatchAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default) =>
        UpdateGraphBatchAsync(entities, new GraphBatchOptions(), cancellationToken);

    public async Task<BatchResult<TKey>> UpdateGraphBatchAsync(
        IEnumerable<TEntity> entities,
        GraphBatchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyGraphResult(stopwatch);
        }

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context) { Logger = _logger, RetryOptions = options.Retry };
        var strategy = BatchStrategyFactory.CreateGraphStrategy<TEntity, TKey>(options.Strategy);
        BatchLogger.LogBatchStarting(_logger, "UpdateGraphBatch", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = await strategy.ExecuteAsync(entityList, strategyContext, options, cancellationToken);

        stopwatch.Stop();

        return EnrichResultWithMetrics(result, stopwatch, strategyContext, "UpdateGraphBatch");
    }

    // === INSERT OPERATIONS ===

    /// <summary>
    /// Inserts a batch of entities using the default strategy (OneByOne).
    /// </summary>
    public InsertBatchResult<TKey> InsertBatch(IEnumerable<TEntity> entities) => InsertBatch(entities, new InsertBatchOptions());

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

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context) { Logger = _logger, RetryOptions = options.Retry };
        var strategy = BatchStrategyFactory.CreateInsertStrategy<TEntity, TKey>(options.Strategy);
        BatchLogger.LogBatchStarting(_logger, "InsertBatch", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichInsertResultWithMetrics(result, stopwatch, strategyContext, "InsertBatch");
    }

    public Task<InsertBatchResult<TKey>> InsertBatchAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default) =>
        InsertBatchAsync(entities, new InsertBatchOptions(), cancellationToken);

    public async Task<InsertBatchResult<TKey>> InsertBatchAsync(
        IEnumerable<TEntity> entities,
        InsertBatchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyInsertResult(stopwatch);
        }

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context) { Logger = _logger, RetryOptions = options.Retry };
        var strategy = BatchStrategyFactory.CreateInsertStrategy<TEntity, TKey>(options.Strategy);
        BatchLogger.LogBatchStarting(_logger, "InsertBatch", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = await strategy.ExecuteAsync(entityList, strategyContext, options, cancellationToken);

        stopwatch.Stop();

        return EnrichInsertResultWithMetrics(result, stopwatch, strategyContext, "InsertBatch");
    }

    /// <summary>
    /// Inserts a batch of entity graphs (parent + children) using the default options.
    /// Each graph succeeds or fails as a unit.
    /// </summary>
    public InsertBatchResult<TKey> InsertGraphBatch(IEnumerable<TEntity> entities) => InsertGraphBatch(entities, new InsertGraphBatchOptions());

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
            return CreateEmptyInsertResult(stopwatch, includeGraph: true);
        }

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context) { Logger = _logger, RetryOptions = options.Retry };
        var strategy = BatchStrategyFactory.CreateInsertGraphStrategy<TEntity, TKey>(options.Strategy);
        BatchLogger.LogBatchStarting(_logger, "InsertGraphBatch", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichInsertResultWithMetrics(result, stopwatch, strategyContext, "InsertGraphBatch");
    }

    public Task<InsertBatchResult<TKey>> InsertGraphBatchAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default) =>
        InsertGraphBatchAsync(entities, new InsertGraphBatchOptions(), cancellationToken);

    public async Task<InsertBatchResult<TKey>> InsertGraphBatchAsync(
        IEnumerable<TEntity> entities,
        InsertGraphBatchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyInsertResult(stopwatch, includeGraph: true);
        }

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context) { Logger = _logger, RetryOptions = options.Retry };
        var strategy = BatchStrategyFactory.CreateInsertGraphStrategy<TEntity, TKey>(options.Strategy);
        BatchLogger.LogBatchStarting(_logger, "InsertGraphBatch", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = await strategy.ExecuteAsync(entityList, strategyContext, options, cancellationToken);

        stopwatch.Stop();

        return EnrichInsertResultWithMetrics(result, stopwatch, strategyContext, "InsertGraphBatch");
    }

    // === DELETE OPERATIONS ===

    /// <summary>
    /// Deletes a batch of entities using the default strategy (OneByOne).
    /// </summary>
    public BatchResult<TKey> DeleteBatch(IEnumerable<TEntity> entities) => DeleteBatch(entities, new DeleteBatchOptions());

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

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context) { Logger = _logger, RetryOptions = options.Retry };
        var strategy = BatchStrategyFactory.CreateDeleteStrategy<TEntity, TKey>(options.Strategy);
        BatchLogger.LogBatchStarting(_logger, "DeleteBatch", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichResultWithMetrics(result, stopwatch, strategyContext, "DeleteBatch");
    }

    public Task<BatchResult<TKey>> DeleteBatchAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default) =>
        DeleteBatchAsync(entities, new DeleteBatchOptions(), cancellationToken);

    public async Task<BatchResult<TKey>> DeleteBatchAsync(
        IEnumerable<TEntity> entities,
        DeleteBatchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyResult(stopwatch);
        }

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context) { Logger = _logger, RetryOptions = options.Retry };
        var strategy = BatchStrategyFactory.CreateDeleteStrategy<TEntity, TKey>(options.Strategy);
        BatchLogger.LogBatchStarting(_logger, "DeleteBatch", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = await strategy.ExecuteAsync(entityList, strategyContext, options, cancellationToken);

        stopwatch.Stop();

        return EnrichResultWithMetrics(result, stopwatch, strategyContext, "DeleteBatch");
    }

    /// <summary>
    /// Deletes a batch of entity graphs (parent + children) using the default options.
    /// Each graph succeeds or fails as a unit.
    /// </summary>
    public BatchResult<TKey> DeleteGraphBatch(IEnumerable<TEntity> entities) => DeleteGraphBatch(entities, new DeleteGraphBatchOptions());

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

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context) { Logger = _logger, RetryOptions = options.Retry };
        var strategy = BatchStrategyFactory.CreateDeleteGraphStrategy<TEntity, TKey>(options.Strategy);
        BatchLogger.LogBatchStarting(_logger, "DeleteGraphBatch", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichResultWithMetrics(result, stopwatch, strategyContext, "DeleteGraphBatch");
    }

    public Task<BatchResult<TKey>> DeleteGraphBatchAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default) =>
        DeleteGraphBatchAsync(entities, new DeleteGraphBatchOptions(), cancellationToken);

    public async Task<BatchResult<TKey>> DeleteGraphBatchAsync(
        IEnumerable<TEntity> entities,
        DeleteGraphBatchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyGraphResult(stopwatch);
        }

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context) { Logger = _logger, RetryOptions = options.Retry };
        var strategy = BatchStrategyFactory.CreateDeleteGraphStrategy<TEntity, TKey>(options.Strategy);
        BatchLogger.LogBatchStarting(_logger, "DeleteGraphBatch", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = await strategy.ExecuteAsync(entityList, strategyContext, options, cancellationToken);

        stopwatch.Stop();

        return EnrichResultWithMetrics(result, stopwatch, strategyContext, "DeleteGraphBatch");
    }

    // === UPSERT OPERATIONS ===

    /// <summary>
    /// Upserts a batch of entities: inserts if key is default, updates if key is non-default.
    /// </summary>
    public UpsertBatchResult<TKey> UpsertBatch(IEnumerable<TEntity> entities) =>
        UpsertBatch(entities, new UpsertBatchOptions());

    /// <summary>
    /// Upserts a batch of entities using the specified options.
    /// </summary>
    public UpsertBatchResult<TKey> UpsertBatch(IEnumerable<TEntity> entities, UpsertBatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
            return CreateEmptyUpsertResult(stopwatch);

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context) { Logger = _logger, RetryOptions = options.Retry };
        var strategy = BatchStrategyFactory.CreateUpsertStrategy<TEntity, TKey>(options.Strategy);
        BatchLogger.LogBatchStarting(_logger, "UpsertBatch", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();
        return EnrichUpsertResultWithMetrics(result, stopwatch, strategyContext, "UpsertBatch");
    }

    public Task<UpsertBatchResult<TKey>> UpsertBatchAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default) =>
        UpsertBatchAsync(entities, new UpsertBatchOptions(), cancellationToken);

    public async Task<UpsertBatchResult<TKey>> UpsertBatchAsync(
        IEnumerable<TEntity> entities,
        UpsertBatchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyUpsertResult(stopwatch);
        }

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context) { Logger = _logger, RetryOptions = options.Retry };
        var strategy = BatchStrategyFactory.CreateUpsertStrategy<TEntity, TKey>(options.Strategy);
        BatchLogger.LogBatchStarting(_logger, "UpsertBatch", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = await strategy.ExecuteAsync(entityList, strategyContext, options, cancellationToken);

        stopwatch.Stop();

        return EnrichUpsertResultWithMetrics(result, stopwatch, strategyContext, "UpsertBatch");
    }

    /// <summary>
    /// Upserts entity graphs (parent + children). Each graph succeeds or fails as a unit.
    /// </summary>
    public UpsertBatchResult<TKey> UpsertGraphBatch(IEnumerable<TEntity> entities) =>
        UpsertGraphBatch(entities, new UpsertGraphBatchOptions());

    /// <summary>
    /// Upserts entity graphs using the specified options.
    /// </summary>
    public UpsertBatchResult<TKey> UpsertGraphBatch(IEnumerable<TEntity> entities, UpsertGraphBatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
            return CreateEmptyUpsertResult(stopwatch, includeGraph: true);

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context) { Logger = _logger, RetryOptions = options.Retry };
        var strategy = BatchStrategyFactory.CreateUpsertGraphStrategy<TEntity, TKey>(options.Strategy);
        BatchLogger.LogBatchStarting(_logger, "UpsertGraphBatch", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();
        return EnrichUpsertResultWithMetrics(result, stopwatch, strategyContext, "UpsertGraphBatch");
    }

    public Task<UpsertBatchResult<TKey>> UpsertGraphBatchAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default) =>
        UpsertGraphBatchAsync(entities, new UpsertGraphBatchOptions(), cancellationToken);

    public async Task<UpsertBatchResult<TKey>> UpsertGraphBatchAsync(
        IEnumerable<TEntity> entities,
        UpsertGraphBatchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyUpsertResult(stopwatch, includeGraph: true);
        }

        var strategyContext = new BatchStrategyContext<TEntity, TKey>(_context) { Logger = _logger, RetryOptions = options.Retry };
        var strategy = BatchStrategyFactory.CreateUpsertGraphStrategy<TEntity, TKey>(options.Strategy);
        BatchLogger.LogBatchStarting(_logger, "UpsertGraphBatch", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = await strategy.ExecuteAsync(entityList, strategyContext, options, cancellationToken);

        stopwatch.Stop();

        return EnrichUpsertResultWithMetrics(result, stopwatch, strategyContext, "UpsertGraphBatch");
    }

    // === PRIVATE HELPERS ===

    private BatchResult<TKey> CreateEmptyResult(Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return BatchResultFactory.CreateEmpty<TKey>(stopwatch.Elapsed);
    }

    private BatchResult<TKey> EnrichResultWithMetrics(
        BatchResult<TKey> result,
        Stopwatch stopwatch,
        BatchStrategyContext<TEntity, TKey> context,
        string operationName)
    {
        var enriched = BatchResultFactory.Enrich(result, stopwatch.Elapsed, context.RoundTripCounter, context.RetryCounter);
        BatchLogger.LogBatchCompleted(_logger, operationName, typeof(TEntity).Name,
            enriched.SuccessCount, enriched.FailureCount, stopwatch.Elapsed.TotalMilliseconds, enriched.DatabaseRoundTrips);
        return enriched;
    }

    private BatchResult<TKey> CreateEmptyGraphResult(Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return BatchResultFactory.CreateEmpty<TKey>(stopwatch.Elapsed, includeGraph: true);
    }

    private InsertBatchResult<TKey> CreateEmptyInsertResult(Stopwatch stopwatch, bool includeGraph = false)
    {
        stopwatch.Stop();
        return BatchResultFactory.CreateEmptyInsert<TKey>(stopwatch.Elapsed, includeGraph);
    }

    private InsertBatchResult<TKey> EnrichInsertResultWithMetrics(
        InsertBatchResult<TKey> result,
        Stopwatch stopwatch,
        BatchStrategyContext<TEntity, TKey> context,
        string operationName)
    {
        var enriched = BatchResultFactory.EnrichInsert(result, stopwatch.Elapsed, context.RoundTripCounter, context.RetryCounter);
        BatchLogger.LogBatchCompleted(_logger, operationName, typeof(TEntity).Name,
            enriched.SuccessCount, enriched.FailureCount, stopwatch.Elapsed.TotalMilliseconds, enriched.DatabaseRoundTrips);
        return enriched;
    }

    private UpsertBatchResult<TKey> CreateEmptyUpsertResult(Stopwatch stopwatch, bool includeGraph = false)
    {
        stopwatch.Stop();
        return BatchResultFactory.CreateEmptyUpsert<TKey>(stopwatch.Elapsed, includeGraph);
    }

    private UpsertBatchResult<TKey> EnrichUpsertResultWithMetrics(
        UpsertBatchResult<TKey> result,
        Stopwatch stopwatch,
        BatchStrategyContext<TEntity, TKey> context,
        string operationName)
    {
        var enriched = BatchResultFactory.EnrichUpsert(result, stopwatch.Elapsed, context.RoundTripCounter, context.RetryCounter);
        BatchLogger.LogBatchCompleted(_logger, operationName, typeof(TEntity).Name,
            enriched.SuccessCount, enriched.FailureCount, stopwatch.Elapsed.TotalMilliseconds, enriched.DatabaseRoundTrips);
        return enriched;
    }
}

/// <summary>
/// Batch saver that automatically detects entity key type at runtime.
/// </summary>
/// <remarks>
/// <para>
/// This overload inspects the DbContext model to determine if the entity has a simple
/// or composite primary key. All results return <see cref="CompositeKey"/> to maintain
/// a consistent API surface.
/// </para>
/// <para>
/// <strong>When to use:</strong> Use this when working with entities that have composite keys,
/// or when the key type isn't known at compile time.
/// </para>
/// <para>
/// <strong>When NOT to use:</strong> For entities with known simple keys (int, long, Guid),
/// prefer <see cref="BatchSaver{TEntity, TKey}"/> for better type safety.
/// </para>
/// <para>
/// <strong>Example (simple key):</strong>
/// <code>
/// var saver = new BatchSaver&lt;Product&gt;(context);
/// var result = saver.InsertBatch(products);
/// int id = result.InsertedIds[0].GetValue&lt;int&gt;(0);
/// </code>
/// </para>
/// <para>
/// <strong>Example (composite key):</strong>
/// <code>
/// var saver = new BatchSaver&lt;OrderLine&gt;(context);
/// var result = saver.InsertBatch(orderLines);
/// int orderId = result.InsertedIds[0].GetValue&lt;int&gt;(0);
/// int lineNum = result.InsertedIds[0].GetValue&lt;int&gt;(1);
/// </code>
/// </para>
/// </remarks>
/// <typeparam name="TEntity">The entity type to save</typeparam>
public class BatchSaver<TEntity> : IBatchSaver<TEntity>
    where TEntity : class
{
    private readonly BatchSaver<TEntity, CompositeKey> _innerSaver;
    private readonly bool _isCompositeKey;

    /// <summary>
    /// Creates a BatchSaver that auto-detects the key type, without logging.
    /// </summary>
    public BatchSaver(DbContext context) : this(context, (ILogger?)null) { }

    /// <summary>
    /// Creates a BatchSaver that auto-detects the key type, with typed logger support (used by DI).
    /// </summary>
    public BatchSaver(DbContext context, ILogger<BatchSaver<TEntity>>? logger)
        : this(context, (ILogger?)logger) { }

    /// <summary>
    /// Creates a BatchSaver that auto-detects the key type, with optional logger support.
    /// </summary>
    public BatchSaver(DbContext context, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(context);

        var entityType = context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException(
                $"Entity type {typeof(TEntity).Name} is not part of the model for this DbContext.");

        var keyProperties = entityType.FindPrimaryKey()?.Properties
            ?? throw new InvalidOperationException(
                $"Entity type {typeof(TEntity).Name} does not have a primary key defined.");

        _isCompositeKey = keyProperties.Count > 1;
        _innerSaver = new BatchSaver<TEntity, CompositeKey>(context, logger);
    }

    /// <inheritdoc />
    public bool IsCompositeKey => _isCompositeKey;

    // === UPDATE OPERATIONS ===

    /// <inheritdoc />
    public BatchResult<CompositeKey> UpdateBatch(IEnumerable<TEntity> entities) =>
        _innerSaver.UpdateBatch(entities);

    /// <inheritdoc />
    public BatchResult<CompositeKey> UpdateBatch(IEnumerable<TEntity> entities, BatchOptions options) =>
        _innerSaver.UpdateBatch(entities, options);

    /// <inheritdoc />
    public Task<BatchResult<CompositeKey>> UpdateBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.UpdateBatchAsync(entities, cancellationToken);

    /// <inheritdoc />
    public Task<BatchResult<CompositeKey>> UpdateBatchAsync(
        IEnumerable<TEntity> entities, BatchOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.UpdateBatchAsync(entities, options, cancellationToken);

    /// <inheritdoc />
    public BatchResult<CompositeKey> UpdateGraphBatch(IEnumerable<TEntity> entities) =>
        _innerSaver.UpdateGraphBatch(entities);

    /// <inheritdoc />
    public BatchResult<CompositeKey> UpdateGraphBatch(IEnumerable<TEntity> entities, GraphBatchOptions options) =>
        _innerSaver.UpdateGraphBatch(entities, options);

    /// <inheritdoc />
    public Task<BatchResult<CompositeKey>> UpdateGraphBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.UpdateGraphBatchAsync(entities, cancellationToken);

    /// <inheritdoc />
    public Task<BatchResult<CompositeKey>> UpdateGraphBatchAsync(
        IEnumerable<TEntity> entities, GraphBatchOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.UpdateGraphBatchAsync(entities, options, cancellationToken);

    // === INSERT OPERATIONS ===

    /// <inheritdoc />
    public InsertBatchResult<CompositeKey> InsertBatch(IEnumerable<TEntity> entities) =>
        _innerSaver.InsertBatch(entities);

    /// <inheritdoc />
    public InsertBatchResult<CompositeKey> InsertBatch(IEnumerable<TEntity> entities, InsertBatchOptions options) =>
        _innerSaver.InsertBatch(entities, options);

    /// <inheritdoc />
    public Task<InsertBatchResult<CompositeKey>> InsertBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.InsertBatchAsync(entities, cancellationToken);

    /// <inheritdoc />
    public Task<InsertBatchResult<CompositeKey>> InsertBatchAsync(
        IEnumerable<TEntity> entities, InsertBatchOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.InsertBatchAsync(entities, options, cancellationToken);

    /// <inheritdoc />
    public InsertBatchResult<CompositeKey> InsertGraphBatch(IEnumerable<TEntity> entities) =>
        _innerSaver.InsertGraphBatch(entities);

    /// <inheritdoc />
    public InsertBatchResult<CompositeKey> InsertGraphBatch(IEnumerable<TEntity> entities, InsertGraphBatchOptions options) =>
        _innerSaver.InsertGraphBatch(entities, options);

    /// <inheritdoc />
    public Task<InsertBatchResult<CompositeKey>> InsertGraphBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.InsertGraphBatchAsync(entities, cancellationToken);

    /// <inheritdoc />
    public Task<InsertBatchResult<CompositeKey>> InsertGraphBatchAsync(
        IEnumerable<TEntity> entities, InsertGraphBatchOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.InsertGraphBatchAsync(entities, options, cancellationToken);

    // === DELETE OPERATIONS ===

    /// <inheritdoc />
    public BatchResult<CompositeKey> DeleteBatch(IEnumerable<TEntity> entities) =>
        _innerSaver.DeleteBatch(entities);

    /// <inheritdoc />
    public BatchResult<CompositeKey> DeleteBatch(IEnumerable<TEntity> entities, DeleteBatchOptions options) =>
        _innerSaver.DeleteBatch(entities, options);

    /// <inheritdoc />
    public Task<BatchResult<CompositeKey>> DeleteBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.DeleteBatchAsync(entities, cancellationToken);

    /// <inheritdoc />
    public Task<BatchResult<CompositeKey>> DeleteBatchAsync(
        IEnumerable<TEntity> entities, DeleteBatchOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.DeleteBatchAsync(entities, options, cancellationToken);

    /// <inheritdoc />
    public BatchResult<CompositeKey> DeleteGraphBatch(IEnumerable<TEntity> entities) =>
        _innerSaver.DeleteGraphBatch(entities);

    /// <inheritdoc />
    public BatchResult<CompositeKey> DeleteGraphBatch(IEnumerable<TEntity> entities, DeleteGraphBatchOptions options) =>
        _innerSaver.DeleteGraphBatch(entities, options);

    /// <inheritdoc />
    public Task<BatchResult<CompositeKey>> DeleteGraphBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.DeleteGraphBatchAsync(entities, cancellationToken);

    /// <inheritdoc />
    public Task<BatchResult<CompositeKey>> DeleteGraphBatchAsync(
        IEnumerable<TEntity> entities, DeleteGraphBatchOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.DeleteGraphBatchAsync(entities, options, cancellationToken);

    // === UPSERT OPERATIONS ===

    /// <inheritdoc />
    public UpsertBatchResult<CompositeKey> UpsertBatch(IEnumerable<TEntity> entities) =>
        _innerSaver.UpsertBatch(entities);

    /// <inheritdoc />
    public UpsertBatchResult<CompositeKey> UpsertBatch(IEnumerable<TEntity> entities, UpsertBatchOptions options) =>
        _innerSaver.UpsertBatch(entities, options);

    /// <inheritdoc />
    public Task<UpsertBatchResult<CompositeKey>> UpsertBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.UpsertBatchAsync(entities, cancellationToken);

    /// <inheritdoc />
    public Task<UpsertBatchResult<CompositeKey>> UpsertBatchAsync(
        IEnumerable<TEntity> entities, UpsertBatchOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.UpsertBatchAsync(entities, options, cancellationToken);

    /// <inheritdoc />
    public UpsertBatchResult<CompositeKey> UpsertGraphBatch(IEnumerable<TEntity> entities) =>
        _innerSaver.UpsertGraphBatch(entities);

    /// <inheritdoc />
    public UpsertBatchResult<CompositeKey> UpsertGraphBatch(
        IEnumerable<TEntity> entities, UpsertGraphBatchOptions options) =>
        _innerSaver.UpsertGraphBatch(entities, options);

    /// <inheritdoc />
    public Task<UpsertBatchResult<CompositeKey>> UpsertGraphBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.UpsertGraphBatchAsync(entities, cancellationToken);

    /// <inheritdoc />
    public Task<UpsertBatchResult<CompositeKey>> UpsertGraphBatchAsync(
        IEnumerable<TEntity> entities, UpsertGraphBatchOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.UpsertGraphBatchAsync(entities, options, cancellationToken);
}
