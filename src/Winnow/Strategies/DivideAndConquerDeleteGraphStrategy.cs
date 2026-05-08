using Winnow.Internal.Accumulators;
using Winnow.Operations;

namespace Winnow.Strategies;

internal class DivideAndConquerDeleteGraphStrategy<TEntity, TKey> : IDeleteGraphStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    public WinnowResult<TKey> Execute(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        DeleteGraphOptions options)
    {
        var operation = BuildOperation(options);
        var strategy = new GenericDivideAndConquerStrategy<TEntity, TKey>();
        return strategy.Execute(entities, context, operation);
    }

    public Task<WinnowResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        DeleteGraphOptions options,
        CancellationToken cancellationToken)
    {
        var operation = BuildOperation(options);
        var strategy = new GenericDivideAndConquerStrategy<TEntity, TKey>();
        return strategy.ExecuteAsync(entities, context, operation, cancellationToken);
    }

    private static DeleteGraphOperation<TEntity, TKey> BuildOperation(DeleteGraphOptions options) =>
        new(options,
            AccumulatorFactory.CreateWinnow<TKey>(options.ResultDetail),
            AccumulatorFactory.CreateGraph<TKey>(options.ResultDetail));
}
