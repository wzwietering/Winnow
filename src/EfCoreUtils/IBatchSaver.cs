namespace EfCoreUtils;

public interface IBatchSaver<TEntity> where TEntity : class
{
    BatchResult UpdateBatch(IEnumerable<TEntity> entities);
    BatchResult UpdateBatch(IEnumerable<TEntity> entities, BatchOptions options);
    Task<BatchResult> UpdateBatchAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<BatchResult> UpdateBatchAsync(IEnumerable<TEntity> entities, BatchOptions options, CancellationToken cancellationToken = default);
}

public class BatchOptions
{
    public BatchStrategy Strategy { get; set; } = BatchStrategy.OneByOne;
}

public enum BatchStrategy
{
    OneByOne,
    DivideAndConquer
}
