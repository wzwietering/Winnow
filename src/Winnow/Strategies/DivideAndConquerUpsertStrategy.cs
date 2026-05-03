using Winnow.Internal.Accumulators;
using Winnow.Operations;

namespace Winnow.Strategies;

internal class DivideAndConquerUpsertStrategy<TEntity, TKey> : IUpsertStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    public UpsertResult<TKey> Execute(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        UpsertOptions options)
    {
        var operation = new UpsertOperation<TEntity, TKey>(options, AccumulatorFactory.CreateUpsert<TKey>(options.ResultDetail));
        var strategy = new GenericDivideAndConquerStrategy<TEntity, TKey>();
        return strategy.ExecuteUpsert(entities, context, operation);
    }

    public Task<UpsertResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        UpsertOptions options,
        CancellationToken cancellationToken)
    {
        var operation = new UpsertOperation<TEntity, TKey>(options, AccumulatorFactory.CreateUpsert<TKey>(options.ResultDetail));
        var strategy = new GenericDivideAndConquerStrategy<TEntity, TKey>();
        return strategy.ExecuteUpsertAsync(entities, context, operation, cancellationToken);
    }
}
