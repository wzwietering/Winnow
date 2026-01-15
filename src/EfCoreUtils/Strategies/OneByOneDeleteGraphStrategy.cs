using EfCoreUtils.Operations;

namespace EfCoreUtils.Strategies;

internal class OneByOneDeleteGraphStrategy<TEntity> : IBatchDeleteGraphStrategy<TEntity> where TEntity : class
{
    public BatchResult Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        DeleteGraphBatchOptions options)
    {
        var operation = new DeleteGraphOperation<TEntity>(options);
        var strategy = new GenericOneByOneStrategy<TEntity>();
        return strategy.Execute(entities, context, operation);
    }
}
