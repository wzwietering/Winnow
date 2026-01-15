using EfCoreUtils.Operations;

namespace EfCoreUtils.Strategies;

internal class DivideAndConquerUpdateStrategy<TEntity> : IBatchUpdateStrategy<TEntity> where TEntity : class
{
    public BatchResult Execute(List<TEntity> entities, BatchStrategyContext<TEntity> context, BatchOptions options)
    {
        var operation = new UpdateOperation<TEntity>(options);
        var strategy = new GenericDivideAndConquerStrategy<TEntity>();
        return strategy.Execute(entities, context, operation);
    }
}
