using EfCoreUtils.Operations;

namespace EfCoreUtils.Strategies;

internal class OneByOneUpdateStrategy<TEntity> : IBatchUpdateStrategy<TEntity> where TEntity : class
{
    public BatchResult Execute(List<TEntity> entities, BatchStrategyContext<TEntity> context, BatchOptions options)
    {
        var operation = new UpdateOperation<TEntity>(options);
        var strategy = new GenericOneByOneStrategy<TEntity>();
        return strategy.Execute(entities, context, operation);
    }
}
