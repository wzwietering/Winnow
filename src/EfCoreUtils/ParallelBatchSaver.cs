using EfCoreUtils.Internal;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils;

/// <summary>
/// Batch saver that executes operations in parallel across multiple DbContext instances.
/// Each partition gets its own DbContext from the factory, enabling true parallel database operations.
/// </summary>
/// <remarks>
/// <para><strong>When to use:</strong> Use when processing large batches where database I/O is the bottleneck.
/// Each partition runs on its own DbContext, so operations are fully isolated.</para>
/// <para><strong>Context factory requirement:</strong> The factory must return a new DbContext instance
/// on each call. Reusing instances across partitions causes concurrency issues.</para>
/// <para><strong>Sync methods:</strong> Run sequentially on a single context (no parallelism).
/// Only async methods benefit from parallel execution.</para>
/// </remarks>
public class ParallelBatchSaver<TEntity, TKey>
    : IBatchSaver<TEntity, TKey>, IDisposable, IAsyncDisposable
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly Func<DbContext> _contextFactory;

    public int MaxDegreeOfParallelism { get; set; }

    public ParallelBatchSaver(Func<DbContext> contextFactory, int maxDegreeOfParallelism = 4)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);

        if (maxDegreeOfParallelism < 1)
            throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism), "Must be at least 1.");

        _contextFactory = contextFactory;
        MaxDegreeOfParallelism = maxDegreeOfParallelism;

        ValidateFactoryCreatesUniqueInstances(contextFactory);
    }

    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // === UPDATE OPERATIONS ===

    public BatchResult<TKey> UpdateBatch(IEnumerable<TEntity> entities) =>
        UpdateBatch(entities, new BatchOptions());

    public BatchResult<TKey> UpdateBatch(IEnumerable<TEntity> entities, BatchOptions options) =>
        ExecuteSync(saver => saver.UpdateBatch(entities, options));

    public Task<BatchResult<TKey>> UpdateBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        UpdateBatchAsync(entities, new BatchOptions(), cancellationToken);

    public Task<BatchResult<TKey>> UpdateBatchAsync(
        IEnumerable<TEntity> entities, BatchOptions options, CancellationToken cancellationToken = default) =>
        RunBatchAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = BatchStrategyFactory.CreateStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, false, cancellationToken);

    // === UPDATE GRAPH OPERATIONS ===

    public BatchResult<TKey> UpdateGraphBatch(IEnumerable<TEntity> entities) =>
        UpdateGraphBatch(entities, new GraphBatchOptions());

    public BatchResult<TKey> UpdateGraphBatch(IEnumerable<TEntity> entities, GraphBatchOptions options) =>
        ExecuteSync(saver => saver.UpdateGraphBatch(entities, options));

    public Task<BatchResult<TKey>> UpdateGraphBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        UpdateGraphBatchAsync(entities, new GraphBatchOptions(), cancellationToken);

    public Task<BatchResult<TKey>> UpdateGraphBatchAsync(
        IEnumerable<TEntity> entities, GraphBatchOptions options, CancellationToken cancellationToken = default) =>
        RunBatchAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = BatchStrategyFactory.CreateGraphStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, true, cancellationToken);

    // === INSERT OPERATIONS ===

    public InsertBatchResult<TKey> InsertBatch(IEnumerable<TEntity> entities) =>
        InsertBatch(entities, new InsertBatchOptions());

    public InsertBatchResult<TKey> InsertBatch(IEnumerable<TEntity> entities, InsertBatchOptions options) =>
        ExecuteSync(saver => saver.InsertBatch(entities, options));

    public Task<InsertBatchResult<TKey>> InsertBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        InsertBatchAsync(entities, new InsertBatchOptions(), cancellationToken);

    public Task<InsertBatchResult<TKey>> InsertBatchAsync(
        IEnumerable<TEntity> entities, InsertBatchOptions options, CancellationToken cancellationToken = default) =>
        RunInsertAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = BatchStrategyFactory.CreateInsertStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, false, cancellationToken);

    // === INSERT GRAPH OPERATIONS ===

    public InsertBatchResult<TKey> InsertGraphBatch(IEnumerable<TEntity> entities) =>
        InsertGraphBatch(entities, new InsertGraphBatchOptions());

    public InsertBatchResult<TKey> InsertGraphBatch(IEnumerable<TEntity> entities, InsertGraphBatchOptions options) =>
        ExecuteSync(saver => saver.InsertGraphBatch(entities, options));

    public Task<InsertBatchResult<TKey>> InsertGraphBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        InsertGraphBatchAsync(entities, new InsertGraphBatchOptions(), cancellationToken);

    public Task<InsertBatchResult<TKey>> InsertGraphBatchAsync(
        IEnumerable<TEntity> entities, InsertGraphBatchOptions options, CancellationToken cancellationToken = default) =>
        RunInsertAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = BatchStrategyFactory.CreateInsertGraphStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, true, cancellationToken);

    // === DELETE OPERATIONS ===

    public BatchResult<TKey> DeleteBatch(IEnumerable<TEntity> entities) =>
        DeleteBatch(entities, new DeleteBatchOptions());

    public BatchResult<TKey> DeleteBatch(IEnumerable<TEntity> entities, DeleteBatchOptions options) =>
        ExecuteSync(saver => saver.DeleteBatch(entities, options));

    public Task<BatchResult<TKey>> DeleteBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        DeleteBatchAsync(entities, new DeleteBatchOptions(), cancellationToken);

    public Task<BatchResult<TKey>> DeleteBatchAsync(
        IEnumerable<TEntity> entities, DeleteBatchOptions options, CancellationToken cancellationToken = default) =>
        RunBatchAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = BatchStrategyFactory.CreateDeleteStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, false, cancellationToken);

    // === DELETE GRAPH OPERATIONS ===

    public BatchResult<TKey> DeleteGraphBatch(IEnumerable<TEntity> entities) =>
        DeleteGraphBatch(entities, new DeleteGraphBatchOptions());

    public BatchResult<TKey> DeleteGraphBatch(IEnumerable<TEntity> entities, DeleteGraphBatchOptions options) =>
        ExecuteSync(saver => saver.DeleteGraphBatch(entities, options));

    public Task<BatchResult<TKey>> DeleteGraphBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        DeleteGraphBatchAsync(entities, new DeleteGraphBatchOptions(), cancellationToken);

    public Task<BatchResult<TKey>> DeleteGraphBatchAsync(
        IEnumerable<TEntity> entities, DeleteGraphBatchOptions options, CancellationToken cancellationToken = default) =>
        RunBatchAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = BatchStrategyFactory.CreateDeleteGraphStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, true, cancellationToken);

    // === UPSERT OPERATIONS ===

    public UpsertBatchResult<TKey> UpsertBatch(IEnumerable<TEntity> entities) =>
        UpsertBatch(entities, new UpsertBatchOptions());

    public UpsertBatchResult<TKey> UpsertBatch(IEnumerable<TEntity> entities, UpsertBatchOptions options) =>
        ExecuteSync(saver => saver.UpsertBatch(entities, options));

    public Task<UpsertBatchResult<TKey>> UpsertBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        UpsertBatchAsync(entities, new UpsertBatchOptions(), cancellationToken);

    public Task<UpsertBatchResult<TKey>> UpsertBatchAsync(
        IEnumerable<TEntity> entities, UpsertBatchOptions options, CancellationToken cancellationToken = default) =>
        RunUpsertAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = BatchStrategyFactory.CreateUpsertStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, false, cancellationToken);

    // === UPSERT GRAPH OPERATIONS ===

    public UpsertBatchResult<TKey> UpsertGraphBatch(IEnumerable<TEntity> entities) =>
        UpsertGraphBatch(entities, new UpsertGraphBatchOptions());

    public UpsertBatchResult<TKey> UpsertGraphBatch(IEnumerable<TEntity> entities, UpsertGraphBatchOptions options) =>
        ExecuteSync(saver => saver.UpsertGraphBatch(entities, options));

    public Task<UpsertBatchResult<TKey>> UpsertGraphBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        UpsertGraphBatchAsync(entities, new UpsertGraphBatchOptions(), cancellationToken);

    public Task<UpsertBatchResult<TKey>> UpsertGraphBatchAsync(
        IEnumerable<TEntity> entities, UpsertGraphBatchOptions options, CancellationToken cancellationToken = default) =>
        RunUpsertAsync(entities, (partition, ctx, ct) =>
        {
            var strategy = BatchStrategyFactory.CreateUpsertGraphStrategy<TEntity, TKey>(options.Strategy);
            return strategy.ExecuteAsync(partition, ctx, options, ct);
        }, true, cancellationToken);

    // === PRIVATE HELPERS ===

    private TResult ExecuteSync<TResult>(Func<BatchSaver<TEntity, TKey>, TResult> operation)
    {
        using var context = _contextFactory();
        var saver = new BatchSaver<TEntity, TKey>(context);
        return operation(saver);
    }

    private async Task<TResult> ExecuteSequentialAsync<TResult>(
        List<TEntity> entityList,
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<TResult>> execute,
        CancellationToken ct)
    {
        var context = _contextFactory();
        try
        {
            var strategyContext = new BatchStrategyContext<TEntity, TKey>(context);
            return await execute(entityList, strategyContext, ct);
        }
        finally
        {
            await context.DisposeAsync();
        }
    }

    private Task<BatchResult<TKey>> RunBatchAsync(
        IEnumerable<TEntity> entities,
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<BatchResult<TKey>>> execute,
        bool includeGraph,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var entityList = entities.ToList();

        if (entityList.Count == 0)
            return Task.FromResult(BatchResultFactory.CreateEmpty<TKey>(TimeSpan.Zero, includeGraph));

        if (MaxDegreeOfParallelism <= 1)
            return ExecuteSequentialAsync(entityList, execute, ct);

        var orchestrator = new ParallelExecutionOrchestrator<TEntity, TKey>(_contextFactory, MaxDegreeOfParallelism);
        return orchestrator.ExecuteBatchAsync(entityList, execute, ct);
    }

    private Task<InsertBatchResult<TKey>> RunInsertAsync(
        IEnumerable<TEntity> entities,
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<InsertBatchResult<TKey>>> execute,
        bool includeGraph,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var entityList = entities.ToList();

        if (entityList.Count == 0)
            return Task.FromResult(BatchResultFactory.CreateEmptyInsert<TKey>(TimeSpan.Zero, includeGraph));

        if (MaxDegreeOfParallelism <= 1)
            return ExecuteSequentialAsync(entityList, execute, ct);

        var orchestrator = new ParallelExecutionOrchestrator<TEntity, TKey>(_contextFactory, MaxDegreeOfParallelism);
        return orchestrator.ExecuteInsertAsync(entityList, execute, ct);
    }

    private Task<UpsertBatchResult<TKey>> RunUpsertAsync(
        IEnumerable<TEntity> entities,
        Func<List<TEntity>, BatchStrategyContext<TEntity, TKey>, CancellationToken, Task<UpsertBatchResult<TKey>>> execute,
        bool includeGraph,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var entityList = entities.ToList();

        if (entityList.Count == 0)
            return Task.FromResult(BatchResultFactory.CreateEmptyUpsert<TKey>(TimeSpan.Zero, includeGraph));

        if (MaxDegreeOfParallelism <= 1)
            return ExecuteSequentialAsync(entityList, execute, ct);

        var orchestrator = new ParallelExecutionOrchestrator<TEntity, TKey>(_contextFactory, MaxDegreeOfParallelism);
        return orchestrator.ExecuteUpsertAsync(entityList, execute, ct);
    }

    private static void ValidateFactoryCreatesUniqueInstances(Func<DbContext> factory)
    {
        DbContext? first = null;
        DbContext? second = null;
        try
        {
            first = factory();
            second = factory();

            if (ReferenceEquals(first, second))
                throw new ArgumentException(
                    "The context factory must return a new DbContext instance on each call. " +
                    "The same instance was returned twice, which would cause concurrency issues.",
                    nameof(factory));
        }
        finally
        {
            first?.Dispose();
            second?.Dispose();
        }
    }
}

/// <summary>
/// Parallel batch saver that automatically detects entity key type at runtime.
/// </summary>
/// <remarks>
/// <para>
/// This overload inspects the DbContext model to determine if the entity has a simple
/// or composite primary key. All results return <see cref="CompositeKey"/> to maintain
/// a consistent API surface.
/// </para>
/// </remarks>
public class ParallelBatchSaver<TEntity>
    : IBatchSaver<TEntity>, IDisposable, IAsyncDisposable
    where TEntity : class
{
    private readonly ParallelBatchSaver<TEntity, CompositeKey> _innerSaver;
    private readonly bool _isCompositeKey;

    public ParallelBatchSaver(Func<DbContext> contextFactory, int maxDegreeOfParallelism = 4)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);

        using var inspectionContext = contextFactory();

        var entityType = inspectionContext.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException(
                $"Entity type {typeof(TEntity).Name} is not part of the model for this DbContext.");

        var keyProperties = entityType.FindPrimaryKey()?.Properties
            ?? throw new InvalidOperationException(
                $"Entity type {typeof(TEntity).Name} does not have a primary key defined.");

        _isCompositeKey = keyProperties.Count > 1;
        _innerSaver = new ParallelBatchSaver<TEntity, CompositeKey>(contextFactory, maxDegreeOfParallelism);
    }

    /// <inheritdoc />
    public bool IsCompositeKey => _isCompositeKey;

    /// <summary>
    /// Maximum number of parallel partitions for async operations.
    /// </summary>
    public int MaxDegreeOfParallelism
    {
        get => _innerSaver.MaxDegreeOfParallelism;
        set => _innerSaver.MaxDegreeOfParallelism = value;
    }

    public void Dispose() => _innerSaver.Dispose();

    public ValueTask DisposeAsync() => _innerSaver.DisposeAsync();

    // === UPDATE OPERATIONS ===

    public BatchResult<CompositeKey> UpdateBatch(IEnumerable<TEntity> entities) =>
        _innerSaver.UpdateBatch(entities);

    public BatchResult<CompositeKey> UpdateBatch(IEnumerable<TEntity> entities, BatchOptions options) =>
        _innerSaver.UpdateBatch(entities, options);

    public Task<BatchResult<CompositeKey>> UpdateBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.UpdateBatchAsync(entities, cancellationToken);

    public Task<BatchResult<CompositeKey>> UpdateBatchAsync(
        IEnumerable<TEntity> entities, BatchOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.UpdateBatchAsync(entities, options, cancellationToken);

    public BatchResult<CompositeKey> UpdateGraphBatch(IEnumerable<TEntity> entities) =>
        _innerSaver.UpdateGraphBatch(entities);

    public BatchResult<CompositeKey> UpdateGraphBatch(IEnumerable<TEntity> entities, GraphBatchOptions options) =>
        _innerSaver.UpdateGraphBatch(entities, options);

    public Task<BatchResult<CompositeKey>> UpdateGraphBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.UpdateGraphBatchAsync(entities, cancellationToken);

    public Task<BatchResult<CompositeKey>> UpdateGraphBatchAsync(
        IEnumerable<TEntity> entities, GraphBatchOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.UpdateGraphBatchAsync(entities, options, cancellationToken);

    // === INSERT OPERATIONS ===

    public InsertBatchResult<CompositeKey> InsertBatch(IEnumerable<TEntity> entities) =>
        _innerSaver.InsertBatch(entities);

    public InsertBatchResult<CompositeKey> InsertBatch(IEnumerable<TEntity> entities, InsertBatchOptions options) =>
        _innerSaver.InsertBatch(entities, options);

    public Task<InsertBatchResult<CompositeKey>> InsertBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.InsertBatchAsync(entities, cancellationToken);

    public Task<InsertBatchResult<CompositeKey>> InsertBatchAsync(
        IEnumerable<TEntity> entities, InsertBatchOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.InsertBatchAsync(entities, options, cancellationToken);

    public InsertBatchResult<CompositeKey> InsertGraphBatch(IEnumerable<TEntity> entities) =>
        _innerSaver.InsertGraphBatch(entities);

    public InsertBatchResult<CompositeKey> InsertGraphBatch(IEnumerable<TEntity> entities, InsertGraphBatchOptions options) =>
        _innerSaver.InsertGraphBatch(entities, options);

    public Task<InsertBatchResult<CompositeKey>> InsertGraphBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.InsertGraphBatchAsync(entities, cancellationToken);

    public Task<InsertBatchResult<CompositeKey>> InsertGraphBatchAsync(
        IEnumerable<TEntity> entities, InsertGraphBatchOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.InsertGraphBatchAsync(entities, options, cancellationToken);

    // === DELETE OPERATIONS ===

    public BatchResult<CompositeKey> DeleteBatch(IEnumerable<TEntity> entities) =>
        _innerSaver.DeleteBatch(entities);

    public BatchResult<CompositeKey> DeleteBatch(IEnumerable<TEntity> entities, DeleteBatchOptions options) =>
        _innerSaver.DeleteBatch(entities, options);

    public Task<BatchResult<CompositeKey>> DeleteBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.DeleteBatchAsync(entities, cancellationToken);

    public Task<BatchResult<CompositeKey>> DeleteBatchAsync(
        IEnumerable<TEntity> entities, DeleteBatchOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.DeleteBatchAsync(entities, options, cancellationToken);

    public BatchResult<CompositeKey> DeleteGraphBatch(IEnumerable<TEntity> entities) =>
        _innerSaver.DeleteGraphBatch(entities);

    public BatchResult<CompositeKey> DeleteGraphBatch(IEnumerable<TEntity> entities, DeleteGraphBatchOptions options) =>
        _innerSaver.DeleteGraphBatch(entities, options);

    public Task<BatchResult<CompositeKey>> DeleteGraphBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.DeleteGraphBatchAsync(entities, cancellationToken);

    public Task<BatchResult<CompositeKey>> DeleteGraphBatchAsync(
        IEnumerable<TEntity> entities, DeleteGraphBatchOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.DeleteGraphBatchAsync(entities, options, cancellationToken);

    // === UPSERT OPERATIONS ===

    public UpsertBatchResult<CompositeKey> UpsertBatch(IEnumerable<TEntity> entities) =>
        _innerSaver.UpsertBatch(entities);

    public UpsertBatchResult<CompositeKey> UpsertBatch(IEnumerable<TEntity> entities, UpsertBatchOptions options) =>
        _innerSaver.UpsertBatch(entities, options);

    public Task<UpsertBatchResult<CompositeKey>> UpsertBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.UpsertBatchAsync(entities, cancellationToken);

    public Task<UpsertBatchResult<CompositeKey>> UpsertBatchAsync(
        IEnumerable<TEntity> entities, UpsertBatchOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.UpsertBatchAsync(entities, options, cancellationToken);

    public UpsertBatchResult<CompositeKey> UpsertGraphBatch(IEnumerable<TEntity> entities) =>
        _innerSaver.UpsertGraphBatch(entities);

    public UpsertBatchResult<CompositeKey> UpsertGraphBatch(
        IEnumerable<TEntity> entities, UpsertGraphBatchOptions options) =>
        _innerSaver.UpsertGraphBatch(entities, options);

    public Task<UpsertBatchResult<CompositeKey>> UpsertGraphBatchAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) =>
        _innerSaver.UpsertGraphBatchAsync(entities, cancellationToken);

    public Task<UpsertBatchResult<CompositeKey>> UpsertGraphBatchAsync(
        IEnumerable<TEntity> entities, UpsertGraphBatchOptions options, CancellationToken cancellationToken = default) =>
        _innerSaver.UpsertGraphBatchAsync(entities, options, cancellationToken);
}
