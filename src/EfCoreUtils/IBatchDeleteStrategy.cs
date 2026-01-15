namespace EfCoreUtils;

/// <summary>
/// Strategy interface for parent-only delete batch operations.
/// </summary>
internal interface IBatchDeleteStrategy<TEntity> where TEntity : class
{
    BatchResult Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        DeleteBatchOptions options);
}

/// <summary>
/// Strategy interface for graph delete batch operations (parent + children).
/// </summary>
internal interface IBatchDeleteGraphStrategy<TEntity> where TEntity : class
{
    BatchResult Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        DeleteGraphBatchOptions options);
}
