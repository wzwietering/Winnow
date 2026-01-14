namespace EfCoreUtils;

public interface IBatchSaver<TEntity> where TEntity : class
{
    BatchResult UpdateBatch(IEnumerable<TEntity> entities);
    BatchResult UpdateBatch(IEnumerable<TEntity> entities, BatchOptions options);
    Task<BatchResult> UpdateBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult> UpdateBatchAsync(IEnumerable<TEntity> entities, BatchOptions options, CancellationToken cancellationToken = default);

    BatchResult UpdateGraphBatch(IEnumerable<TEntity> entities);
    BatchResult UpdateGraphBatch(IEnumerable<TEntity> entities, GraphBatchOptions options);
    Task<BatchResult> UpdateGraphBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult> UpdateGraphBatchAsync(IEnumerable<TEntity> entities, GraphBatchOptions options, CancellationToken cancellationToken = default);
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
