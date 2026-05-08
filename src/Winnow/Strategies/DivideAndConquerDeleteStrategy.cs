using Winnow.Internal.Accumulators;
using Winnow.Operations;

namespace Winnow.Strategies;

internal class DivideAndConquerDeleteStrategy<TEntity, TKey> : IDeleteStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    public WinnowResult<TKey> Execute(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        DeleteOptions options)
    {
        var operation = new DeleteOperation<TEntity, TKey>(options, AccumulatorFactory.CreateWinnow<TKey>(options.ResultDetail));
        var strategy = new GenericDivideAndConquerStrategy<TEntity, TKey>();
        return strategy.Execute(entities, context, operation);
    }

    public Task<WinnowResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        DeleteOptions options,
        CancellationToken cancellationToken)
    {
        var operation = new DeleteOperation<TEntity, TKey>(options, AccumulatorFactory.CreateWinnow<TKey>(options.ResultDetail));
        var strategy = new GenericDivideAndConquerStrategy<TEntity, TKey>();
        return strategy.ExecuteAsync(entities, context, operation, cancellationToken);
    }
}
