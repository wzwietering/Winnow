using Winnow.Internal.Accumulators;
using Winnow.Operations;

namespace Winnow.Strategies;

internal class OneByOneUpsertGraphStrategy<TEntity, TKey> : IUpsertGraphStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    public UpsertResult<TKey> Execute(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        UpsertGraphOptions options)
    {
        var operation = BuildOperation(options);
        var strategy = new GenericOneByOneStrategy<TEntity, TKey>();
        return strategy.ExecuteUpsert(entities, context, operation);
    }

    public Task<UpsertResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        UpsertGraphOptions options,
        CancellationToken cancellationToken)
    {
        var operation = BuildOperation(options);
        var strategy = new GenericOneByOneStrategy<TEntity, TKey>();
        return strategy.ExecuteUpsertAsync(entities, context, operation, cancellationToken);
    }

    private static UpsertGraphOperation<TEntity, TKey> BuildOperation(UpsertGraphOptions options) =>
        new(options,
            AccumulatorFactory.CreateUpsert<TKey>(options.ResultDetail),
            AccumulatorFactory.CreateGraph<TKey>(options.ResultDetail));
}
