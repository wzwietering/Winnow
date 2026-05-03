using Winnow.Internal.Accumulators;
using Winnow.Operations;

namespace Winnow.Strategies;

internal class DivideAndConquerInsertGraphStrategy<TEntity, TKey> : IInsertGraphStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    public InsertResult<TKey> Execute(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        InsertGraphOptions options)
    {
        var operation = BuildOperation(options);
        var strategy = new GenericDivideAndConquerStrategy<TEntity, TKey>();
        return strategy.ExecuteInsert(entities, context, operation);
    }

    public Task<InsertResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        InsertGraphOptions options,
        CancellationToken cancellationToken)
    {
        var operation = BuildOperation(options);
        var strategy = new GenericDivideAndConquerStrategy<TEntity, TKey>();
        return strategy.ExecuteInsertAsync(entities, context, operation, cancellationToken);
    }

    private static InsertGraphOperation<TEntity, TKey> BuildOperation(InsertGraphOptions options) =>
        new(options,
            AccumulatorFactory.CreateInsert<TKey>(options.ResultDetail),
            AccumulatorFactory.CreateGraph<TKey>(options.ResultDetail));
}
