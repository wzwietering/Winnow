using EfCoreUtils.Operations;

namespace EfCoreUtils.Strategies;

internal class OneByOneInsertStrategy<TEntity, TKey> : IBatchInsertStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    public InsertBatchResult<TKey> Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        InsertBatchOptions options)
    {
        var operation = new InsertOperation<TEntity, TKey>(options);
        var strategy = new GenericOneByOneStrategy<TEntity, TKey>();
        return strategy.ExecuteInsert(entities, context, operation);
    }

    public Task<InsertBatchResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        InsertBatchOptions options,
        CancellationToken cancellationToken)
    {
        var operation = new InsertOperation<TEntity, TKey>(options);
        var strategy = new GenericOneByOneStrategy<TEntity, TKey>();
        return strategy.ExecuteInsertAsync(entities, context, operation, cancellationToken);
    }
}
