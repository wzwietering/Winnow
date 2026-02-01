namespace EfCoreUtils;

/// <summary>
/// Strategy interface for parent-only delete batch operations.
/// </summary>
internal interface IBatchDeleteStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    BatchResult<TKey> Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        DeleteBatchOptions options);

    Task<BatchResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        DeleteBatchOptions options,
        CancellationToken cancellationToken);
}

/// <summary>
/// Strategy interface for graph delete batch operations (parent + children).
/// </summary>
internal interface IBatchDeleteGraphStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    BatchResult<TKey> Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        DeleteGraphBatchOptions options);

    Task<BatchResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        DeleteGraphBatchOptions options,
        CancellationToken cancellationToken);
}
