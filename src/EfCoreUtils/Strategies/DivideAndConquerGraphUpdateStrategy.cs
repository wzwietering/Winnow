using EfCoreUtils.Operations;

namespace EfCoreUtils.Strategies;

internal class DivideAndConquerGraphUpdateStrategy<TEntity> : IBatchGraphUpdateStrategy<TEntity>
    where TEntity : class
{
    public BatchResult Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        GraphBatchOptions options)
    {
        var operation = new UpdateGraphOperation<TEntity>(options);
        var strategy = new GenericDivideAndConquerStrategy<TEntity>();
        return strategy.Execute(entities, context, operation);
    }
}
