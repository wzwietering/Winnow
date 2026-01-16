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
