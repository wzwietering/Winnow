namespace EfCoreUtils;

internal interface IBatchInsertStrategy<TEntity> where TEntity : class
{
    InsertBatchResult Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        InsertBatchOptions options);
}

internal interface IBatchInsertGraphStrategy<TEntity> where TEntity : class
{
    InsertBatchResult Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        InsertGraphBatchOptions options);
}
