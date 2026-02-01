using EfCoreUtils.Operations;

namespace EfCoreUtils.Strategies;

internal class OneByOneInsertGraphStrategy<TEntity, TKey> : IBatchInsertGraphStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    public InsertBatchResult<TKey> Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        InsertGraphBatchOptions options)
    {
        var operation = new InsertGraphOperation<TEntity, TKey>(options);
        var strategy = new GenericOneByOneStrategy<TEntity, TKey>();
        return strategy.ExecuteInsert(entities, context, operation);
    }

    public Task<InsertBatchResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        InsertGraphBatchOptions options,
        CancellationToken cancellationToken)
    {
        var operation = new InsertGraphOperation<TEntity, TKey>(options);
        var strategy = new GenericOneByOneStrategy<TEntity, TKey>();
        return strategy.ExecuteInsertAsync(entities, context, operation, cancellationToken);
    }
}
