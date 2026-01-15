namespace EfCoreUtils;

public interface IBatchSaver<TEntity> where TEntity : class
{
    // === UPDATE OPERATIONS ===

    BatchResult UpdateBatch(IEnumerable<TEntity> entities);
    BatchResult UpdateBatch(IEnumerable<TEntity> entities, BatchOptions options);
    Task<BatchResult> UpdateBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult> UpdateBatchAsync(IEnumerable<TEntity> entities, BatchOptions options, CancellationToken cancellationToken = default);

    BatchResult UpdateGraphBatch(IEnumerable<TEntity> entities);
    BatchResult UpdateGraphBatch(IEnumerable<TEntity> entities, GraphBatchOptions options);
    Task<BatchResult> UpdateGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult> UpdateGraphBatchAsync(IEnumerable<TEntity> entities, GraphBatchOptions options, CancellationToken cancellationToken = default);

    // === INSERT OPERATIONS ===

    /// <summary>
    /// Insert entities individually with failure isolation.
    /// </summary>
    InsertBatchResult InsertBatch(IEnumerable<TEntity> entities);
    InsertBatchResult InsertBatch(IEnumerable<TEntity> entities, InsertBatchOptions options);
    Task<InsertBatchResult> InsertBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<InsertBatchResult> InsertBatchAsync(IEnumerable<TEntity> entities, InsertBatchOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert entity graphs (parent + children) with failure isolation.
    /// </summary>
    InsertBatchResult InsertGraphBatch(IEnumerable<TEntity> entities);
    InsertBatchResult InsertGraphBatch(IEnumerable<TEntity> entities, InsertGraphBatchOptions options);
    Task<InsertBatchResult> InsertGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<InsertBatchResult> InsertGraphBatchAsync(IEnumerable<TEntity> entities, InsertGraphBatchOptions options, CancellationToken cancellationToken = default);
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
