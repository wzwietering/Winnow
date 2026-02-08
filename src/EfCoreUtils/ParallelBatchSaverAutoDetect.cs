using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils;

/// <summary>
/// Parallel batch saver that automatically detects entity key type at runtime.
/// </summary>
/// <remarks>
/// <para>
/// This overload inspects the DbContext model to determine if the entity has a simple
/// or composite primary key. All results return <see cref="CompositeKey"/> to maintain
/// a consistent API surface.
/// </para>
/// <para>The constructor creates a DbContext from the factory once to inspect the key type.
/// The context is immediately disposed after inspection.</para>
/// </remarks>
public class ParallelBatchSaver<TEntity>
    : IBatchSaver<TEntity>
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

    /// <inheritdoc cref="ParallelBatchSaver{TEntity, TKey}.MaxDegreeOfParallelism"/>
    public int MaxDegreeOfParallelism => _innerSaver.MaxDegreeOfParallelism;

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
