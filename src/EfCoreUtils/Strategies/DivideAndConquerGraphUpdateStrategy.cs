using EfCoreUtils.Operations;

namespace EfCoreUtils.Strategies;

internal class DivideAndConquerGraphUpdateStrategy<TEntity, TKey> : IBatchGraphUpdateStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    public BatchResult<TKey> Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        GraphBatchOptions options)
    {
        var operation = new UpdateGraphOperation<TEntity, TKey>(options);
        var strategy = new GenericDivideAndConquerStrategy<TEntity, TKey>();
        return strategy.Execute(entities, context, operation);
    }
}
