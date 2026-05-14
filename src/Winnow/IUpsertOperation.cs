namespace Winnow;

/// <summary>
/// Defines operation-specific behavior for upsert batch processing strategies.
/// Uses index tracking (like IInsertOperation) since some entities may be new.
/// </summary>
internal interface IUpsertOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    void ValidateAll(List<TEntity> entities, StrategyContext<TEntity, TKey> context);
    void PrepareEntity(TEntity entity, int index, StrategyContext<TEntity, TKey> context);
    void RecordSuccess(TEntity entity, int index, StrategyContext<TEntity, TKey> context);
    void RecordFailure(TEntity entity, int index, Exception ex, StrategyContext<TEntity, TKey> context);
    void CleanupEntity(TEntity entity, StrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Runs any pre-batch resolution that needs to happen before per-entity preparation
    /// (e.g. MatchBy SELECT). Default for operations that have no pre-resolution is a no-op.
    /// </summary>
    void ResolveBatch(List<TEntity> entities, StrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Async counterpart of <see cref="ResolveBatch"/>.
    /// </summary>
    Task ResolveBatchAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Re-queries an existing row matching the entity's MatchBy values and, when found,
    /// copies the primary key and any concurrency-token values from the row onto the entity.
    /// Returns false when MatchBy is not configured or no row matches.
    /// Used by the duplicate-key retry path so it can flip a failed INSERT to MODIFIED
    /// even when the original detection was business-key based.
    /// </summary>
    bool TryRefreshFromMatchBy(TEntity entity, StrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Async counterpart of <see cref="TryRefreshFromMatchBy"/>. Used by the async retry path
    /// so the refresh SELECT goes through async I/O and observes the cancellation token.
    /// </summary>
    Task<bool> TryRefreshFromMatchByAsync(
        TEntity entity,
        StrategyContext<TEntity, TKey> context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates the final result from tracked successes and failures.
    /// </summary>
    /// <param name="wasCancelled">Whether the operation was cancelled before completing.</param>
    UpsertResult<TKey> CreateResult(bool wasCancelled = false);

    /// <summary>
    /// Returns true if the entity at the given index was prepared as an INSERT.
    /// </summary>
    bool WasInsertAttempt(int index);

    /// <summary>
    /// Gets the duplicate key strategy from options.
    /// </summary>
    DuplicateKeyStrategy DuplicateKeyStrategy { get; }

    /// <summary>
    /// Records a successful update after retry (was originally planned as insert).
    /// </summary>
    void RecordSuccessAsUpdate(TEntity entity, int index, StrategyContext<TEntity, TKey> context);
}
