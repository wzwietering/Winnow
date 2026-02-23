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
