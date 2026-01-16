using EfCoreUtils.Operations;

namespace EfCoreUtils.Strategies;

internal class DivideAndConquerUpdateStrategy<TEntity, TKey> : IBatchUpdateStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    public BatchResult<TKey> Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        BatchOptions options)
    {
        var operation = new UpdateOperation<TEntity, TKey>(options);
        var strategy = new GenericDivideAndConquerStrategy<TEntity, TKey>();
        return strategy.Execute(entities, context, operation);
    }
}
