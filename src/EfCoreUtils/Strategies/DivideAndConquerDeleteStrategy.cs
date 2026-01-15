using EfCoreUtils.Operations;

namespace EfCoreUtils.Strategies;

internal class DivideAndConquerDeleteStrategy<TEntity> : IBatchDeleteStrategy<TEntity> where TEntity : class
{
    public BatchResult Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        DeleteBatchOptions options)
    {
        var operation = new DeleteOperation<TEntity>(options);
        var strategy = new GenericDivideAndConquerStrategy<TEntity>();
        return strategy.Execute(entities, context, operation);
    }
}
