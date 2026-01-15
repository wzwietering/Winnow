using EfCoreUtils.Operations;

namespace EfCoreUtils.Strategies;

internal class OneByOneDeleteStrategy<TEntity> : IBatchDeleteStrategy<TEntity> where TEntity : class
{
    public BatchResult Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        DeleteBatchOptions options)
    {
        var operation = new DeleteOperation<TEntity>(options);
        var strategy = new GenericOneByOneStrategy<TEntity>();
        return strategy.Execute(entities, context, operation);
    }
}
