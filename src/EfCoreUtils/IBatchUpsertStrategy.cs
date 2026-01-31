namespace EfCoreUtils;

/// <summary>
/// Strategy interface for parent-only upsert batch operations.
/// </summary>
internal interface IBatchUpsertStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    UpsertBatchResult<TKey> Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        UpsertBatchOptions options);
}

/// <summary>
/// Strategy interface for graph upsert batch operations (parent + children).
/// </summary>
internal interface IBatchUpsertGraphStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    UpsertBatchResult<TKey> Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        UpsertGraphBatchOptions options);
}
