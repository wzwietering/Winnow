namespace Winnow;

/// <summary>
/// Strategy interface for parent-only upsert batch operations.
/// </summary>
internal interface IUpsertStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    UpsertResult<TKey> Execute(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        UpsertOptions options);

    Task<UpsertResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        UpsertOptions options,
        CancellationToken cancellationToken);
}

/// <summary>
/// Strategy interface for graph upsert batch operations (parent + children).
/// </summary>
internal interface IUpsertGraphStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    UpsertResult<TKey> Execute(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        UpsertGraphOptions options);

    Task<UpsertResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        UpsertGraphOptions options,
        CancellationToken cancellationToken);
}
