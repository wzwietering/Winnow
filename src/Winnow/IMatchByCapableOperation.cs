using Winnow.Internal;

namespace Winnow;

/// <summary>
/// Marker interface for upsert operations that support MatchBy resolution.
/// Strategies and the duplicate-key retry path use this via <c>as</c>-cast to
/// decide whether to invoke the MatchBy pre-SELECT and retry-refresh hooks,
/// so operations that don't support MatchBy (e.g. graph upsert) carry no
/// no-op stubs for these members.
/// </summary>
internal interface IMatchByCapableOperation<TEntity, TKey> : IUpsertOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    /// <summary>
    /// Runs the MatchBy pre-SELECT and caches the resolved row map for the duration
    /// of the current batch. Called once by the strategy before per-entity preparation.
    /// No-op when <see cref="UpsertOptions.MatchBy"/> is null.
    /// </summary>
    /// <param name="entities">The survivor list (post pre-validation).</param>
    /// <param name="originalIndices">Original input position of each survivor, or
    /// <c>null</c> when no pre-validation was applied (identity).</param>
    /// <param name="inputCount">Original caller-supplied input count, used to size
    /// the match-value array so per-entity routing can index by original position.</param>
    /// <param name="context">Strategy context.</param>
    void ResolveBatch(
        List<TEntity> entities,
        int[]? originalIndices,
        int inputCount,
        StrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Async counterpart of <see cref="ResolveBatch"/>.
    /// </summary>
    Task ResolveBatchAsync(
        List<TEntity> entities,
        int[]? originalIndices,
        int inputCount,
        StrategyContext<TEntity, TKey> context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Re-queries an existing row matching the entity's MatchBy values and, when found,
    /// copies the primary key and any concurrency-token values from the row onto the entity.
    /// Used by the duplicate-key retry path so it can flip a failed INSERT to MODIFIED
    /// even when the original detection was business-key based.
    /// </summary>
    MatchByRefreshOutcome TryRefreshFromMatchBy(TEntity entity, StrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Async counterpart of <see cref="TryRefreshFromMatchBy"/>.
    /// </summary>
    Task<MatchByRefreshOutcome> TryRefreshFromMatchByAsync(
        TEntity entity,
        StrategyContext<TEntity, TKey> context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records a failure for an entity whose duplicate-key retry could not be recovered
    /// because the MatchBy refresh found no matching row — classified as
    /// <see cref="FailureReason.BusinessKeyConflictLost"/>.
    /// </summary>
    void RecordBusinessKeyConflictLost(TEntity entity, int index, StrategyContext<TEntity, TKey> context);
}
