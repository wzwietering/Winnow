using Winnow.Operations;

namespace Winnow.Strategies;

internal class OneByOneUpdateStrategy<TEntity, TKey> : IUpdateStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    public WinnowResult<TKey> Execute(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        WinnowOptions options)
    {
        var operation = new UpdateOperation<TEntity, TKey>(options);
        var strategy = new GenericOneByOneStrategy<TEntity, TKey>();
        return strategy.Execute(entities, context, operation);
    }

    public Task<WinnowResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        WinnowOptions options,
        CancellationToken cancellationToken)
    {
        var operation = new UpdateOperation<TEntity, TKey>(options);
        var strategy = new GenericOneByOneStrategy<TEntity, TKey>();
        return strategy.ExecuteAsync(entities, context, operation, cancellationToken);
    }
}
