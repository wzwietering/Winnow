namespace EfCoreUtils;

internal interface IBatchInsertStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    InsertBatchResult<TKey> Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        InsertBatchOptions options);
}

internal interface IBatchInsertGraphStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    InsertBatchResult<TKey> Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        InsertGraphBatchOptions options);
}
