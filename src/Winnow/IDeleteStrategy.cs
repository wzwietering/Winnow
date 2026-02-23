namespace Winnow;

/// <summary>
/// Strategy interface for parent-only delete batch operations.
/// </summary>
internal interface IDeleteStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    WinnowResult<TKey> Execute(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        DeleteOptions options);

    Task<WinnowResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        DeleteOptions options,
        CancellationToken cancellationToken);
}

/// <summary>
/// Strategy interface for graph delete batch operations (parent + children).
/// </summary>
internal interface IDeleteGraphStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    WinnowResult<TKey> Execute(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        DeleteGraphOptions options);

    Task<WinnowResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        DeleteGraphOptions options,
        CancellationToken cancellationToken);
}
