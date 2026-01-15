using EfCoreUtils.Operations;

namespace EfCoreUtils.Strategies;

internal class OneByOneInsertGraphStrategy<TEntity> : IBatchInsertGraphStrategy<TEntity> where TEntity : class
{
    public InsertBatchResult Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        InsertGraphBatchOptions options)
    {
        var operation = new InsertGraphOperation<TEntity>(options);
        var strategy = new GenericOneByOneStrategy<TEntity>();
        return strategy.ExecuteInsert(entities, context, operation);
    }
}
