using Winnow.Operations;

namespace Winnow.Strategies;

internal class OneByOneInsertGraphStrategy<TEntity, TKey> : IInsertGraphStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    public InsertResult<TKey> Execute(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        InsertGraphOptions options)
    {
        var operation = new InsertGraphOperation<TEntity, TKey>(options);
        var strategy = new GenericOneByOneStrategy<TEntity, TKey>();
        return strategy.ExecuteInsert(entities, context, operation);
    }

    public Task<InsertResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        InsertGraphOptions options,
        CancellationToken cancellationToken)
    {
        var operation = new InsertGraphOperation<TEntity, TKey>(options);
        var strategy = new GenericOneByOneStrategy<TEntity, TKey>();
        return strategy.ExecuteInsertAsync(entities, context, operation, cancellationToken);
    }
}
