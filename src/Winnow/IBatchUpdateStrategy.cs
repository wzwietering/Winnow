namespace Winnow;

internal interface IBatchUpdateStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    BatchResult<TKey> Execute(List<TEntity> entities, BatchStrategyContext<TEntity, TKey> context, BatchOptions options);

    Task<BatchResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        BatchOptions options,
        CancellationToken cancellationToken);
}
