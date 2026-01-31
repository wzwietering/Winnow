using EfCoreUtils.Operations;

namespace EfCoreUtils.Strategies;

internal class OneByOneUpsertGraphStrategy<TEntity, TKey> : IBatchUpsertGraphStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    public UpsertBatchResult<TKey> Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        UpsertGraphBatchOptions options)
    {
        var operation = new UpsertGraphOperation<TEntity, TKey>(options);
        var strategy = new GenericOneByOneStrategy<TEntity, TKey>();
        return strategy.ExecuteUpsert(entities, context, operation);
    }
}
