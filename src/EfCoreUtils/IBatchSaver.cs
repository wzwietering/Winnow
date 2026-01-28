namespace EfCoreUtils;

public interface IBatchSaver<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    // === UPDATE OPERATIONS ===

    BatchResult<TKey> UpdateBatch(IEnumerable<TEntity> entities);
    BatchResult<TKey> UpdateBatch(IEnumerable<TEntity> entities, BatchOptions options);
    Task<BatchResult<TKey>> UpdateBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult<TKey>> UpdateBatchAsync(IEnumerable<TEntity> entities, BatchOptions options, CancellationToken cancellationToken = default);

    BatchResult<TKey> UpdateGraphBatch(IEnumerable<TEntity> entities);
    BatchResult<TKey> UpdateGraphBatch(IEnumerable<TEntity> entities, GraphBatchOptions options);
    Task<BatchResult<TKey>> UpdateGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult<TKey>> UpdateGraphBatchAsync(IEnumerable<TEntity> entities, GraphBatchOptions options, CancellationToken cancellationToken = default);

    // === INSERT OPERATIONS ===

    /// <summary>
    /// Insert entities individually with failure isolation.
    /// </summary>
    InsertBatchResult<TKey> InsertBatch(IEnumerable<TEntity> entities);
    InsertBatchResult<TKey> InsertBatch(IEnumerable<TEntity> entities, InsertBatchOptions options);
    Task<InsertBatchResult<TKey>> InsertBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<InsertBatchResult<TKey>> InsertBatchAsync(IEnumerable<TEntity> entities, InsertBatchOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert entity graphs (parent + children) with failure isolation.
    /// </summary>
    InsertBatchResult<TKey> InsertGraphBatch(IEnumerable<TEntity> entities);
    InsertBatchResult<TKey> InsertGraphBatch(IEnumerable<TEntity> entities, InsertGraphBatchOptions options);
    Task<InsertBatchResult<TKey>> InsertGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<InsertBatchResult<TKey>> InsertGraphBatchAsync(IEnumerable<TEntity> entities, InsertGraphBatchOptions options, CancellationToken cancellationToken = default);

    // === DELETE OPERATIONS ===

    /// <summary>
    /// Delete entities individually with failure isolation.
    /// </summary>
    BatchResult<TKey> DeleteBatch(IEnumerable<TEntity> entities);
    BatchResult<TKey> DeleteBatch(IEnumerable<TEntity> entities, DeleteBatchOptions options);
    Task<BatchResult<TKey>> DeleteBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult<TKey>> DeleteBatchAsync(IEnumerable<TEntity> entities, DeleteBatchOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete entity graphs (parent + children) with failure isolation.
    /// </summary>
    BatchResult<TKey> DeleteGraphBatch(IEnumerable<TEntity> entities);
    BatchResult<TKey> DeleteGraphBatch(IEnumerable<TEntity> entities, DeleteGraphBatchOptions options);
    Task<BatchResult<TKey>> DeleteGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult<TKey>> DeleteGraphBatchAsync(IEnumerable<TEntity> entities, DeleteGraphBatchOptions options, CancellationToken cancellationToken = default);
}

/// <summary>
/// Batch saver interface with auto-detected key type.
/// All keys are returned as CompositeKey - use key.GetValue&lt;T&gt;(0) for simple keys.
/// </summary>
public interface IBatchSaver<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Returns true if the entity has a composite primary key (more than one column).
    /// </summary>
    bool IsCompositeKey { get; }

    // === UPDATE OPERATIONS ===

    BatchResult<CompositeKey> UpdateBatch(IEnumerable<TEntity> entities);
    BatchResult<CompositeKey> UpdateBatch(IEnumerable<TEntity> entities, BatchOptions options);
    Task<BatchResult<CompositeKey>> UpdateBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult<CompositeKey>> UpdateBatchAsync(IEnumerable<TEntity> entities, BatchOptions options, CancellationToken cancellationToken = default);

    BatchResult<CompositeKey> UpdateGraphBatch(IEnumerable<TEntity> entities);
    BatchResult<CompositeKey> UpdateGraphBatch(IEnumerable<TEntity> entities, GraphBatchOptions options);
    Task<BatchResult<CompositeKey>> UpdateGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult<CompositeKey>> UpdateGraphBatchAsync(IEnumerable<TEntity> entities, GraphBatchOptions options, CancellationToken cancellationToken = default);

    // === INSERT OPERATIONS ===

    InsertBatchResult<CompositeKey> InsertBatch(IEnumerable<TEntity> entities);
    InsertBatchResult<CompositeKey> InsertBatch(IEnumerable<TEntity> entities, InsertBatchOptions options);
    Task<InsertBatchResult<CompositeKey>> InsertBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<InsertBatchResult<CompositeKey>> InsertBatchAsync(IEnumerable<TEntity> entities, InsertBatchOptions options, CancellationToken cancellationToken = default);

    InsertBatchResult<CompositeKey> InsertGraphBatch(IEnumerable<TEntity> entities);
    InsertBatchResult<CompositeKey> InsertGraphBatch(IEnumerable<TEntity> entities, InsertGraphBatchOptions options);
    Task<InsertBatchResult<CompositeKey>> InsertGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<InsertBatchResult<CompositeKey>> InsertGraphBatchAsync(IEnumerable<TEntity> entities, InsertGraphBatchOptions options, CancellationToken cancellationToken = default);

    // === DELETE OPERATIONS ===

    BatchResult<CompositeKey> DeleteBatch(IEnumerable<TEntity> entities);
    BatchResult<CompositeKey> DeleteBatch(IEnumerable<TEntity> entities, DeleteBatchOptions options);
    Task<BatchResult<CompositeKey>> DeleteBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult<CompositeKey>> DeleteBatchAsync(IEnumerable<TEntity> entities, DeleteBatchOptions options, CancellationToken cancellationToken = default);

    BatchResult<CompositeKey> DeleteGraphBatch(IEnumerable<TEntity> entities);
    BatchResult<CompositeKey> DeleteGraphBatch(IEnumerable<TEntity> entities, DeleteGraphBatchOptions options);
    Task<BatchResult<CompositeKey>> DeleteGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult<CompositeKey>> DeleteGraphBatchAsync(IEnumerable<TEntity> entities, DeleteGraphBatchOptions options, CancellationToken cancellationToken = default);
}

public class BatchOptions
{
    public BatchStrategy Strategy { get; set; } = BatchStrategy.OneByOne;

    /// <summary>
    /// When true (default), validates that navigation properties are not modified.
    /// Set to false to allow navigation properties to be loaded but ignored.
    /// </summary>
    public bool ValidateNavigationProperties { get; set; } = true;
}

public enum BatchStrategy
{
    OneByOne,
    DivideAndConquer
}
