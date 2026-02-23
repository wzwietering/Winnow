using Winnow.Operations;

namespace Winnow.Strategies;

internal class DivideAndConquerInsertStrategy<TEntity, TKey> : IInsertStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    public InsertResult<TKey> Execute(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        InsertOptions options)
    {
        var operation = new InsertOperation<TEntity, TKey>(options);
        var strategy = new GenericDivideAndConquerStrategy<TEntity, TKey>();
        return strategy.ExecuteInsert(entities, context, operation);
    }

    public Task<InsertResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        InsertOptions options,
        CancellationToken cancellationToken)
    {
        var operation = new InsertOperation<TEntity, TKey>(options);
        var strategy = new GenericDivideAndConquerStrategy<TEntity, TKey>();
        return strategy.ExecuteInsertAsync(entities, context, operation, cancellationToken);
    }
}
