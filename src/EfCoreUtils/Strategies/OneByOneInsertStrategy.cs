using EfCoreUtils.Operations;

namespace EfCoreUtils.Strategies;

internal class OneByOneInsertStrategy<TEntity> : IBatchInsertStrategy<TEntity> where TEntity : class
{
    public InsertBatchResult Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        InsertBatchOptions options)
    {
        var operation = new InsertOperation<TEntity>(options);
        var strategy = new GenericOneByOneStrategy<TEntity>();
        return strategy.ExecuteInsert(entities, context, operation);
    }
}
