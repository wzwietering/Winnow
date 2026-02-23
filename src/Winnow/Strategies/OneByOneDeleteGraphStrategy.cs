using Winnow.Operations;

namespace Winnow.Strategies;

internal class OneByOneDeleteGraphStrategy<TEntity, TKey> : IDeleteGraphStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    public WinnowResult<TKey> Execute(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        DeleteGraphOptions options)
    {
        var operation = new DeleteGraphOperation<TEntity, TKey>(options);
        var strategy = new GenericOneByOneStrategy<TEntity, TKey>();
        return strategy.Execute(entities, context, operation);
    }

    public Task<WinnowResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        DeleteGraphOptions options,
        CancellationToken cancellationToken)
    {
        var operation = new DeleteGraphOperation<TEntity, TKey>(options);
        var strategy = new GenericOneByOneStrategy<TEntity, TKey>();
        return strategy.ExecuteAsync(entities, context, operation, cancellationToken);
    }
}
