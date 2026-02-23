namespace Winnow;

internal interface IGraphUpdateStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    WinnowResult<TKey> Execute(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        GraphOptions options);

    Task<WinnowResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        GraphOptions options,
        CancellationToken cancellationToken);
}
