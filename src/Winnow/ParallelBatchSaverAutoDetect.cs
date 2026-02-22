using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Winnow;

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

    /// <summary>
    /// Creates a ParallelBatchSaver that auto-detects the key type.
    /// </summary>
    /// <param name="contextFactory">Factory that creates a new DbContext on each call.</param>
    /// <param name="maxDegreeOfParallelism">Maximum parallel partitions (default: 4, minimum: 1).</param>
    /// <param name="logger">Optional logger for operation diagnostics.</param>
    public ParallelBatchSaver(
        Func<DbContext> contextFactory,
        int maxDegreeOfParallelism = 4,
        ILogger? logger = null)
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
        _innerSaver = new ParallelBatchSaver<TEntity, CompositeKey>(contextFactory, maxDegreeOfParallelism, logger);
    }

    /// <inheritdoc />
    public bool IsCompositeKey => _isCompositeKey;

    /// <inheritdoc cref="ParallelBatchSaver{TEntity, TKey}.MaxDegreeOfParallelism"/>
    public int MaxDegreeOfParallelism => _innerSaver.MaxDegreeOfParallelism;

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
