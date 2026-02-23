namespace Winnow;

internal interface IUpdateStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    WinnowResult<TKey> Execute(List<TEntity> entities, StrategyContext<TEntity, TKey> context, WinnowOptions options);

    Task<WinnowResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        WinnowOptions options,
        CancellationToken cancellationToken);
}
