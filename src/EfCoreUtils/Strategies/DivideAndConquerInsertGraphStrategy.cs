using EfCoreUtils.Operations;

namespace EfCoreUtils.Strategies;

internal class DivideAndConquerInsertGraphStrategy<TEntity> : IBatchInsertGraphStrategy<TEntity> where TEntity : class
{
    public InsertBatchResult Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        InsertGraphBatchOptions options)
    {
        var operation = new InsertGraphOperation<TEntity>(options);
        var strategy = new GenericDivideAndConquerStrategy<TEntity>();
        return strategy.ExecuteInsert(entities, context, operation);
    }
}
