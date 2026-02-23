using System.Diagnostics;
using Winnow.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Winnow;

/// <summary>
/// Batch saver that processes entities with failure isolation.
/// </summary>
public class Winnower<TEntity, TKey> : IWinnower<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly DbContext _context;
    private readonly ILogger? _logger;

    /// <summary>
    /// Creates a Winnower without logging.
    /// </summary>
    public Winnower(DbContext context) : this(context, (ILogger?)null) { }

    /// <summary>
    /// Creates a Winnower with typed logger support (used by DI).
    /// </summary>
    public Winnower(DbContext context, ILogger<Winnower<TEntity, TKey>>? logger)
        : this(context, (ILogger?)logger) { }

    /// <summary>
    /// Creates a Winnower with optional untyped logger support.
    /// </summary>
    internal Winnower(DbContext context, ILogger? logger)
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
    public WinnowResult<TKey> Update(IEnumerable<TEntity> entities) => Update(entities, new WinnowOptions());

    /// <summary>
    /// Updates a batch of entities using the specified strategy and options.
    /// Only properties of TEntity are updated. Navigation properties are NOT updated
    /// even if loaded with .Include(). For entity graph updates, use standard EF Core SaveChanges().
    /// </summary>
    /// <param name="entities">The entities to update</param>
    /// <param name="options">Batch operation options</param>
    /// <returns>Result containing successful IDs, failures, and performance metrics</returns>
    /// <exception cref="InvalidOperationException">Thrown when navigation properties are modified and ValidateNavigationProperties is true</exception>
    public WinnowResult<TKey> Update(IEnumerable<TEntity> entities, WinnowOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyResult(stopwatch);
        }

        var strategyContext = CreateStrategyContext(options.Retry);
        var strategy = StrategyFactory.CreateStrategy<TEntity, TKey>(options.Strategy);
        WinnowLogger.LogStarting(_logger, "Update", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichResultWithMetrics(result, stopwatch, strategyContext, "Update");
    }

    /// <inheritdoc />
    public Task<WinnowResult<TKey>> UpdateAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        UpdateAsync(entities, new WinnowOptions(), cancellationToken);

    /// <inheritdoc />
    public async Task<WinnowResult<TKey>> UpdateAsync(IEnumerable<TEntity> entities, WinnowOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyResult(stopwatch);
        }

        var strategyContext = CreateStrategyContext(options.Retry);
        var strategy = StrategyFactory.CreateStrategy<TEntity, TKey>(options.Strategy);
        WinnowLogger.LogStarting(_logger, "Update", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = await strategy.ExecuteAsync(entityList, strategyContext, options, cancellationToken);

        stopwatch.Stop();

        return EnrichResultWithMetrics(result, stopwatch, strategyContext, "Update");
    }

    /// <summary>
    /// Updates a batch of entity graphs (parent + children) using the default options.
    /// Each graph succeeds or fails as a unit - if any entity in a graph fails, the entire graph rolls back.
    /// </summary>
    /// <param name="entities">The parent entities with their navigation properties loaded</param>
    /// <returns>Result containing successful IDs, failures, child IDs by parent, and performance metrics</returns>
    public WinnowResult<TKey> UpdateGraph(IEnumerable<TEntity> entities) => UpdateGraph(entities, new GraphOptions());

    /// <summary>
    /// Updates a batch of entity graphs (parent + children) using the specified options.
    /// Each graph succeeds or fails as a unit - if any entity in a graph fails, the entire graph rolls back.
    /// </summary>
    /// <param name="entities">The parent entities with their navigation properties loaded</param>
    /// <param name="options">Graph batch operation options</param>
    /// <returns>Result containing successful IDs, failures, child IDs by parent, and performance metrics</returns>
    public WinnowResult<TKey> UpdateGraph(IEnumerable<TEntity> entities, GraphOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyGraphResult(stopwatch);
        }

        var strategyContext = CreateStrategyContext(options.Retry);
        var strategy = StrategyFactory.CreateGraphStrategy<TEntity, TKey>(options.Strategy);
        WinnowLogger.LogStarting(_logger, "UpdateGraph", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichResultWithMetrics(result, stopwatch, strategyContext, "UpdateGraph");
    }

    /// <inheritdoc />
    public Task<WinnowResult<TKey>> UpdateGraphAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default) =>
        UpdateGraphAsync(entities, new GraphOptions(), cancellationToken);

    /// <inheritdoc />
    public async Task<WinnowResult<TKey>> UpdateGraphAsync(
        IEnumerable<TEntity> entities,
        GraphOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyGraphResult(stopwatch);
        }

        var strategyContext = CreateStrategyContext(options.Retry);
        var strategy = StrategyFactory.CreateGraphStrategy<TEntity, TKey>(options.Strategy);
        WinnowLogger.LogStarting(_logger, "UpdateGraph", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = await strategy.ExecuteAsync(entityList, strategyContext, options, cancellationToken);

        stopwatch.Stop();

        return EnrichResultWithMetrics(result, stopwatch, strategyContext, "UpdateGraph");
    }

    // === INSERT OPERATIONS ===

    /// <summary>
    /// Inserts a batch of entities using the default strategy (OneByOne).
    /// </summary>
    public InsertResult<TKey> Insert(IEnumerable<TEntity> entities) => Insert(entities, new InsertOptions());

    /// <summary>
    /// Inserts a batch of entities using the specified strategy and options.
    /// </summary>
    public InsertResult<TKey> Insert(IEnumerable<TEntity> entities, InsertOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyInsertResult(stopwatch);
        }

        var strategyContext = CreateStrategyContext(options.Retry);
        var strategy = StrategyFactory.CreateInsertStrategy<TEntity, TKey>(options.Strategy);
        WinnowLogger.LogStarting(_logger, "Insert", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichInsertResultWithMetrics(result, stopwatch, strategyContext, "Insert");
    }

    /// <inheritdoc />
    public Task<InsertResult<TKey>> InsertAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default) =>
        InsertAsync(entities, new InsertOptions(), cancellationToken);

    /// <inheritdoc />
    public async Task<InsertResult<TKey>> InsertAsync(
        IEnumerable<TEntity> entities,
        InsertOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyInsertResult(stopwatch);
        }

        var strategyContext = CreateStrategyContext(options.Retry);
        var strategy = StrategyFactory.CreateInsertStrategy<TEntity, TKey>(options.Strategy);
        WinnowLogger.LogStarting(_logger, "Insert", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = await strategy.ExecuteAsync(entityList, strategyContext, options, cancellationToken);

        stopwatch.Stop();

        return EnrichInsertResultWithMetrics(result, stopwatch, strategyContext, "Insert");
    }

    /// <summary>
    /// Inserts a batch of entity graphs (parent + children) using the default options.
    /// Each graph succeeds or fails as a unit.
    /// </summary>
    public InsertResult<TKey> InsertGraph(IEnumerable<TEntity> entities) => InsertGraph(entities, new InsertGraphOptions());

    /// <summary>
    /// Inserts a batch of entity graphs (parent + children) using the specified options.
    /// Each graph succeeds or fails as a unit.
    /// </summary>
    public InsertResult<TKey> InsertGraph(IEnumerable<TEntity> entities, InsertGraphOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyInsertResult(stopwatch, includeGraph: true);
        }

        var strategyContext = CreateStrategyContext(options.Retry);
        var strategy = StrategyFactory.CreateInsertGraphStrategy<TEntity, TKey>(options.Strategy);
        WinnowLogger.LogStarting(_logger, "InsertGraph", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichInsertResultWithMetrics(result, stopwatch, strategyContext, "InsertGraph");
    }

    /// <inheritdoc />
    public Task<InsertResult<TKey>> InsertGraphAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default) =>
        InsertGraphAsync(entities, new InsertGraphOptions(), cancellationToken);

    /// <inheritdoc />
    public async Task<InsertResult<TKey>> InsertGraphAsync(
        IEnumerable<TEntity> entities,
        InsertGraphOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyInsertResult(stopwatch, includeGraph: true);
        }

        var strategyContext = CreateStrategyContext(options.Retry);
        var strategy = StrategyFactory.CreateInsertGraphStrategy<TEntity, TKey>(options.Strategy);
        WinnowLogger.LogStarting(_logger, "InsertGraph", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = await strategy.ExecuteAsync(entityList, strategyContext, options, cancellationToken);

        stopwatch.Stop();

        return EnrichInsertResultWithMetrics(result, stopwatch, strategyContext, "InsertGraph");
    }

    // === DELETE OPERATIONS ===

    /// <summary>
    /// Deletes a batch of entities using the default strategy (OneByOne).
    /// </summary>
    public WinnowResult<TKey> Delete(IEnumerable<TEntity> entities) => Delete(entities, new DeleteOptions());

    /// <summary>
    /// Deletes a batch of entities using the specified strategy and options.
    /// </summary>
    public WinnowResult<TKey> Delete(IEnumerable<TEntity> entities, DeleteOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyResult(stopwatch);
        }

        var strategyContext = CreateStrategyContext(options.Retry);
        var strategy = StrategyFactory.CreateDeleteStrategy<TEntity, TKey>(options.Strategy);
        WinnowLogger.LogStarting(_logger, "Delete", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichResultWithMetrics(result, stopwatch, strategyContext, "Delete");
    }

    /// <inheritdoc />
    public Task<WinnowResult<TKey>> DeleteAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default) =>
        DeleteAsync(entities, new DeleteOptions(), cancellationToken);

    /// <inheritdoc />
    public async Task<WinnowResult<TKey>> DeleteAsync(
        IEnumerable<TEntity> entities,
        DeleteOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyResult(stopwatch);
        }

        var strategyContext = CreateStrategyContext(options.Retry);
        var strategy = StrategyFactory.CreateDeleteStrategy<TEntity, TKey>(options.Strategy);
        WinnowLogger.LogStarting(_logger, "Delete", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = await strategy.ExecuteAsync(entityList, strategyContext, options, cancellationToken);

        stopwatch.Stop();

        return EnrichResultWithMetrics(result, stopwatch, strategyContext, "Delete");
    }

    /// <summary>
    /// Deletes a batch of entity graphs (parent + children) using the default options.
    /// Each graph succeeds or fails as a unit.
    /// </summary>
    public WinnowResult<TKey> DeleteGraph(IEnumerable<TEntity> entities) => DeleteGraph(entities, new DeleteGraphOptions());

    /// <summary>
    /// Deletes a batch of entity graphs (parent + children) using the specified options.
    /// Each graph succeeds or fails as a unit.
    /// </summary>
    public WinnowResult<TKey> DeleteGraph(IEnumerable<TEntity> entities, DeleteGraphOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyGraphResult(stopwatch);
        }

        var strategyContext = CreateStrategyContext(options.Retry);
        var strategy = StrategyFactory.CreateDeleteGraphStrategy<TEntity, TKey>(options.Strategy);
        WinnowLogger.LogStarting(_logger, "DeleteGraph", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();

        return EnrichResultWithMetrics(result, stopwatch, strategyContext, "DeleteGraph");
    }

    /// <inheritdoc />
    public Task<WinnowResult<TKey>> DeleteGraphAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default) =>
        DeleteGraphAsync(entities, new DeleteGraphOptions(), cancellationToken);

    /// <inheritdoc />
    public async Task<WinnowResult<TKey>> DeleteGraphAsync(
        IEnumerable<TEntity> entities,
        DeleteGraphOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyGraphResult(stopwatch);
        }

        var strategyContext = CreateStrategyContext(options.Retry);
        var strategy = StrategyFactory.CreateDeleteGraphStrategy<TEntity, TKey>(options.Strategy);
        WinnowLogger.LogStarting(_logger, "DeleteGraph", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = await strategy.ExecuteAsync(entityList, strategyContext, options, cancellationToken);

        stopwatch.Stop();

        return EnrichResultWithMetrics(result, stopwatch, strategyContext, "DeleteGraph");
    }

    // === UPSERT OPERATIONS ===

    /// <summary>
    /// Upserts a batch of entities: inserts if key is default, updates if key is non-default.
    /// </summary>
    public UpsertResult<TKey> Upsert(IEnumerable<TEntity> entities) =>
        Upsert(entities, new UpsertOptions());

    /// <summary>
    /// Upserts a batch of entities using the specified options.
    /// </summary>
    public UpsertResult<TKey> Upsert(IEnumerable<TEntity> entities, UpsertOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
            return CreateEmptyUpsertResult(stopwatch);

        var strategyContext = CreateStrategyContext(options.Retry);
        var strategy = StrategyFactory.CreateUpsertStrategy<TEntity, TKey>(options.Strategy);
        WinnowLogger.LogStarting(_logger, "Upsert", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();
        return EnrichUpsertResultWithMetrics(result, stopwatch, strategyContext, "Upsert");
    }

    /// <inheritdoc />
    public Task<UpsertResult<TKey>> UpsertAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default) =>
        UpsertAsync(entities, new UpsertOptions(), cancellationToken);

    /// <inheritdoc />
    public async Task<UpsertResult<TKey>> UpsertAsync(
        IEnumerable<TEntity> entities,
        UpsertOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyUpsertResult(stopwatch);
        }

        var strategyContext = CreateStrategyContext(options.Retry);
        var strategy = StrategyFactory.CreateUpsertStrategy<TEntity, TKey>(options.Strategy);
        WinnowLogger.LogStarting(_logger, "Upsert", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = await strategy.ExecuteAsync(entityList, strategyContext, options, cancellationToken);

        stopwatch.Stop();

        return EnrichUpsertResultWithMetrics(result, stopwatch, strategyContext, "Upsert");
    }

    /// <summary>
    /// Upserts entity graphs (parent + children). Each graph succeeds or fails as a unit.
    /// </summary>
    public UpsertResult<TKey> UpsertGraph(IEnumerable<TEntity> entities) =>
        UpsertGraph(entities, new UpsertGraphOptions());

    /// <summary>
    /// Upserts entity graphs using the specified options.
    /// </summary>
    public UpsertResult<TKey> UpsertGraph(IEnumerable<TEntity> entities, UpsertGraphOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
            return CreateEmptyUpsertResult(stopwatch, includeGraph: true);

        var strategyContext = CreateStrategyContext(options.Retry);
        var strategy = StrategyFactory.CreateUpsertGraphStrategy<TEntity, TKey>(options.Strategy);
        WinnowLogger.LogStarting(_logger, "UpsertGraph", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = strategy.Execute(entityList, strategyContext, options);

        stopwatch.Stop();
        return EnrichUpsertResultWithMetrics(result, stopwatch, strategyContext, "UpsertGraph");
    }

    /// <inheritdoc />
    public Task<UpsertResult<TKey>> UpsertGraphAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default) =>
        UpsertGraphAsync(entities, new UpsertGraphOptions(), cancellationToken);

    /// <inheritdoc />
    public async Task<UpsertResult<TKey>> UpsertGraphAsync(
        IEnumerable<TEntity> entities,
        UpsertGraphOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);

        var stopwatch = Stopwatch.StartNew();
        var entityList = entities.ToList();

        if (entityList.Count == 0)
        {
            return CreateEmptyUpsertResult(stopwatch, includeGraph: true);
        }

        var strategyContext = CreateStrategyContext(options.Retry);
        var strategy = StrategyFactory.CreateUpsertGraphStrategy<TEntity, TKey>(options.Strategy);
        WinnowLogger.LogStarting(_logger, "UpsertGraph", typeof(TEntity).Name, entityList.Count, options.Strategy);
        var result = await strategy.ExecuteAsync(entityList, strategyContext, options, cancellationToken);

        stopwatch.Stop();

        return EnrichUpsertResultWithMetrics(result, stopwatch, strategyContext, "UpsertGraph");
    }

    // === PRIVATE HELPERS ===

    private StrategyContext<TEntity, TKey> CreateStrategyContext(RetryOptions? retry) =>
        new(_context) { Logger = _logger, RetryOptions = retry };

    private WinnowResult<TKey> CreateEmptyResult(Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return ResultFactory.CreateEmpty<TKey>(stopwatch.Elapsed);
    }

    private WinnowResult<TKey> EnrichResultWithMetrics(
        WinnowResult<TKey> result,
        Stopwatch stopwatch,
        StrategyContext<TEntity, TKey> context,
        string operationName)
    {
        var enriched = ResultFactory.Enrich(result, stopwatch.Elapsed, context.RoundTripCounter, context.RetryCounter);
        WinnowLogger.LogCompleted(_logger, operationName, typeof(TEntity).Name,
            enriched.SuccessCount, enriched.FailureCount, stopwatch.Elapsed.TotalMilliseconds, enriched.DatabaseRoundTrips);
        return enriched;
    }

    private WinnowResult<TKey> CreateEmptyGraphResult(Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return ResultFactory.CreateEmpty<TKey>(stopwatch.Elapsed, includeGraph: true);
    }

    private InsertResult<TKey> CreateEmptyInsertResult(Stopwatch stopwatch, bool includeGraph = false)
    {
        stopwatch.Stop();
        return ResultFactory.CreateEmptyInsert<TKey>(stopwatch.Elapsed, includeGraph);
    }

    private InsertResult<TKey> EnrichInsertResultWithMetrics(
        InsertResult<TKey> result,
        Stopwatch stopwatch,
        StrategyContext<TEntity, TKey> context,
        string operationName)
    {
        var enriched = ResultFactory.EnrichInsert(result, stopwatch.Elapsed, context.RoundTripCounter, context.RetryCounter);
        WinnowLogger.LogCompleted(_logger, operationName, typeof(TEntity).Name,
            enriched.SuccessCount, enriched.FailureCount, stopwatch.Elapsed.TotalMilliseconds, enriched.DatabaseRoundTrips);
        return enriched;
    }

    private UpsertResult<TKey> CreateEmptyUpsertResult(Stopwatch stopwatch, bool includeGraph = false)
    {
        stopwatch.Stop();
        return ResultFactory.CreateEmptyUpsert<TKey>(stopwatch.Elapsed, includeGraph);
    }

    private UpsertResult<TKey> EnrichUpsertResultWithMetrics(
        UpsertResult<TKey> result,
        Stopwatch stopwatch,
        StrategyContext<TEntity, TKey> context,
        string operationName)
    {
        var enriched = ResultFactory.EnrichUpsert(result, stopwatch.Elapsed, context.RoundTripCounter, context.RetryCounter);
        WinnowLogger.LogCompleted(_logger, operationName, typeof(TEntity).Name,
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
/// prefer <see cref="Winnower{TEntity, TKey}"/> for better type safety.
/// </para>
/// <para>
/// <strong>Example (simple key):</strong>
/// <code>
/// var saver = new Winnower&lt;Product&gt;(context);
/// var result = saver.Insert(products);
/// int id = result.InsertedIds[0].GetValue&lt;int&gt;(0);
/// </code>
/// </para>
/// <para>
/// <strong>Example (composite key):</strong>
/// <code>
/// var saver = new Winnower&lt;OrderLine&gt;(context);
/// var result = saver.Insert(orderLines);
/// int orderId = result.InsertedIds[0].GetValue&lt;int&gt;(0);
/// int lineNum = result.InsertedIds[0].GetValue&lt;int&gt;(1);
/// </code>
/// </para>
/// </remarks>
/// <typeparam name="TEntity">The entity type to save</typeparam>
public class Winnower<TEntity> : IWinnower<TEntity>
    where TEntity : class
{
    private readonly Winnower<TEntity, CompositeKey> _innerSaver;
    private readonly bool _isCompositeKey;

    /// <summary>
    /// Creates a Winnower that auto-detects the key type, without logging.
    /// </summary>
    public Winnower(DbContext context) : this(context, (ILogger?)null) { }

    /// <summary>
    /// Creates a Winnower that auto-detects the key type, with typed logger support (used by DI).
    /// </summary>
    public Winnower(DbContext context, ILogger<Winnower<TEntity>>? logger)
        : this(context, (ILogger?)logger) { }

    /// <summary>
    /// Creates a Winnower that auto-detects the key type, with optional untyped logger support.
    /// </summary>
    internal Winnower(DbContext context, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(context);

        var entityType = context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException(
                $"Entity type {typeof(TEntity).Name} is not part of the model for this DbContext.");

        var keyProperties = entityType.FindPrimaryKey()?.Properties
            ?? throw new InvalidOperationException(
                $"Entity type {typeof(TEntity).Name} does not have a primary key defined.");

        _isCompositeKey = keyProperties.Count > 1;
        _innerSaver = new Winnower<TEntity, CompositeKey>(context, logger);
    }

    /// <inheritdoc />
    public bool IsCompositeKey => _isCompositeKey;

    // === UPDATE OPERATIONS ===

    /// <inheritdoc />
    public WinnowResult<CompositeKey> Update(IEnumerable<TEntity> entities) =>
        _innerSaver.Update(entities);

    /// <inheritdoc />
    public WinnowResult<CompositeKey> Update(IEnumerable<TEntity> entities, WinnowOptions options) =>
        _innerSaver.Update(entities, options);

    /// <inheritdoc />
    public Task<WinnowResult<CompositeKey>> UpdateAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.UpdateAsync(entities, cancellationToken);

    /// <inheritdoc />
    public Task<WinnowResult<CompositeKey>> UpdateAsync(
        IEnumerable<TEntity> entities, WinnowOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.UpdateAsync(entities, options, cancellationToken);

    /// <inheritdoc />
    public WinnowResult<CompositeKey> UpdateGraph(IEnumerable<TEntity> entities) =>
        _innerSaver.UpdateGraph(entities);

    /// <inheritdoc />
    public WinnowResult<CompositeKey> UpdateGraph(IEnumerable<TEntity> entities, GraphOptions options) =>
        _innerSaver.UpdateGraph(entities, options);

    /// <inheritdoc />
    public Task<WinnowResult<CompositeKey>> UpdateGraphAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.UpdateGraphAsync(entities, cancellationToken);

    /// <inheritdoc />
    public Task<WinnowResult<CompositeKey>> UpdateGraphAsync(
        IEnumerable<TEntity> entities, GraphOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.UpdateGraphAsync(entities, options, cancellationToken);

    // === INSERT OPERATIONS ===

    /// <inheritdoc />
    public InsertResult<CompositeKey> Insert(IEnumerable<TEntity> entities) =>
        _innerSaver.Insert(entities);

    /// <inheritdoc />
    public InsertResult<CompositeKey> Insert(IEnumerable<TEntity> entities, InsertOptions options) =>
        _innerSaver.Insert(entities, options);

    /// <inheritdoc />
    public Task<InsertResult<CompositeKey>> InsertAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.InsertAsync(entities, cancellationToken);

    /// <inheritdoc />
    public Task<InsertResult<CompositeKey>> InsertAsync(
        IEnumerable<TEntity> entities, InsertOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.InsertAsync(entities, options, cancellationToken);

    /// <inheritdoc />
    public InsertResult<CompositeKey> InsertGraph(IEnumerable<TEntity> entities) =>
        _innerSaver.InsertGraph(entities);

    /// <inheritdoc />
    public InsertResult<CompositeKey> InsertGraph(IEnumerable<TEntity> entities, InsertGraphOptions options) =>
        _innerSaver.InsertGraph(entities, options);

    /// <inheritdoc />
    public Task<InsertResult<CompositeKey>> InsertGraphAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.InsertGraphAsync(entities, cancellationToken);

    /// <inheritdoc />
    public Task<InsertResult<CompositeKey>> InsertGraphAsync(
        IEnumerable<TEntity> entities, InsertGraphOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.InsertGraphAsync(entities, options, cancellationToken);

    // === DELETE OPERATIONS ===

    /// <inheritdoc />
    public WinnowResult<CompositeKey> Delete(IEnumerable<TEntity> entities) =>
        _innerSaver.Delete(entities);

    /// <inheritdoc />
    public WinnowResult<CompositeKey> Delete(IEnumerable<TEntity> entities, DeleteOptions options) =>
        _innerSaver.Delete(entities, options);

    /// <inheritdoc />
    public Task<WinnowResult<CompositeKey>> DeleteAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.DeleteAsync(entities, cancellationToken);

    /// <inheritdoc />
    public Task<WinnowResult<CompositeKey>> DeleteAsync(
        IEnumerable<TEntity> entities, DeleteOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.DeleteAsync(entities, options, cancellationToken);

    /// <inheritdoc />
    public WinnowResult<CompositeKey> DeleteGraph(IEnumerable<TEntity> entities) =>
        _innerSaver.DeleteGraph(entities);

    /// <inheritdoc />
    public WinnowResult<CompositeKey> DeleteGraph(IEnumerable<TEntity> entities, DeleteGraphOptions options) =>
        _innerSaver.DeleteGraph(entities, options);

    /// <inheritdoc />
    public Task<WinnowResult<CompositeKey>> DeleteGraphAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.DeleteGraphAsync(entities, cancellationToken);

    /// <inheritdoc />
    public Task<WinnowResult<CompositeKey>> DeleteGraphAsync(
        IEnumerable<TEntity> entities, DeleteGraphOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.DeleteGraphAsync(entities, options, cancellationToken);

    // === UPSERT OPERATIONS ===

    /// <inheritdoc />
    public UpsertResult<CompositeKey> Upsert(IEnumerable<TEntity> entities) =>
        _innerSaver.Upsert(entities);

    /// <inheritdoc />
    public UpsertResult<CompositeKey> Upsert(IEnumerable<TEntity> entities, UpsertOptions options) =>
        _innerSaver.Upsert(entities, options);

    /// <inheritdoc />
    public Task<UpsertResult<CompositeKey>> UpsertAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.UpsertAsync(entities, cancellationToken);

    /// <inheritdoc />
    public Task<UpsertResult<CompositeKey>> UpsertAsync(
        IEnumerable<TEntity> entities, UpsertOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.UpsertAsync(entities, options, cancellationToken);

    /// <inheritdoc />
    public UpsertResult<CompositeKey> UpsertGraph(IEnumerable<TEntity> entities) =>
        _innerSaver.UpsertGraph(entities);

    /// <inheritdoc />
    public UpsertResult<CompositeKey> UpsertGraph(
        IEnumerable<TEntity> entities, UpsertGraphOptions options) =>
        _innerSaver.UpsertGraph(entities, options);

    /// <inheritdoc />
    public Task<UpsertResult<CompositeKey>> UpsertGraphAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.UpsertGraphAsync(entities, cancellationToken);

    /// <inheritdoc />
    public Task<UpsertResult<CompositeKey>> UpsertGraphAsync(
        IEnumerable<TEntity> entities, UpsertGraphOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.UpsertGraphAsync(entities, options, cancellationToken);
}
