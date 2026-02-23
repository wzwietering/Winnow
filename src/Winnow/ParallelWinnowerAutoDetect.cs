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
public class ParallelWinnower<TEntity>
    : IWinnower<TEntity>
    where TEntity : class
{
    private readonly ParallelWinnower<TEntity, CompositeKey> _innerSaver;
    private readonly bool _isCompositeKey;

    /// <summary>
    /// Creates a ParallelWinnower that auto-detects the key type.
    /// </summary>
    /// <param name="contextFactory">Factory that creates a new DbContext on each call.</param>
    /// <param name="maxDegreeOfParallelism">Maximum parallel partitions (default: 4, minimum: 1).</param>
    /// <param name="logger">Optional logger for operation diagnostics.</param>
    public ParallelWinnower(
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
        _innerSaver = new ParallelWinnower<TEntity, CompositeKey>(contextFactory, maxDegreeOfParallelism, logger);
    }

    /// <inheritdoc />
    public bool IsCompositeKey => _isCompositeKey;

    /// <inheritdoc cref="ParallelWinnower{TEntity, TKey}.MaxDegreeOfParallelism"/>
    public int MaxDegreeOfParallelism => _innerSaver.MaxDegreeOfParallelism;

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
