using EfCoreUtils.Operations;

namespace EfCoreUtils.Strategies;

internal class DivideAndConquerDeleteStrategy<TEntity, TKey> : IBatchDeleteStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    public BatchResult<TKey> Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        DeleteBatchOptions options)
    {
        var operation = new DeleteOperation<TEntity, TKey>(options);
        var strategy = new GenericDivideAndConquerStrategy<TEntity, TKey>();
        return strategy.Execute(entities, context, operation);
    }
}
