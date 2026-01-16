using EfCoreUtils.Operations;

namespace EfCoreUtils.Strategies;

internal class DivideAndConquerDeleteGraphStrategy<TEntity, TKey> : IBatchDeleteGraphStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    public BatchResult<TKey> Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        DeleteGraphBatchOptions options)
    {
        var operation = new DeleteGraphOperation<TEntity, TKey>(options);
        var strategy = new GenericDivideAndConquerStrategy<TEntity, TKey>();
        return strategy.Execute(entities, context, operation);
    }
}
