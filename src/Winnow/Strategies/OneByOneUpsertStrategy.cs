using Winnow.Operations;

namespace Winnow.Strategies;

internal class OneByOneUpsertStrategy<TEntity, TKey> : IBatchUpsertStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    public UpsertBatchResult<TKey> Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        UpsertBatchOptions options)
    {
        var operation = new UpsertOperation<TEntity, TKey>(options);
        var strategy = new GenericOneByOneStrategy<TEntity, TKey>();
        return strategy.ExecuteUpsert(entities, context, operation);
    }

    public Task<UpsertBatchResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        UpsertBatchOptions options,
        CancellationToken cancellationToken)
    {
        var operation = new UpsertOperation<TEntity, TKey>(options);
        var strategy = new GenericOneByOneStrategy<TEntity, TKey>();
        return strategy.ExecuteUpsertAsync(entities, context, operation, cancellationToken);
    }
}
