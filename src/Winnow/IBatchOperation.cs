namespace Winnow;

/// <summary>
/// Defines operation-specific behavior for batch processing strategies.
/// Strategies (OneByOne, DivideAndConquer) use this interface to delegate
/// operation-specific logic (Update, Insert, Delete) while reusing the algorithm.
/// </summary>
internal interface IBatchOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    /// <summary>
    /// Validates all entities before processing. Called once at the start.
    /// </summary>
    void ValidateAll(List<TEntity> entities, BatchStrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Prepares a single entity for the database operation (e.g., sets entity state).
    /// </summary>
    void PrepareEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Records a successful operation after SaveChanges succeeds.
    /// </summary>
    void RecordSuccess(TEntity entity, BatchStrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Records a failed operation after SaveChanges fails.
    /// </summary>
    void RecordFailure(TEntity entity, Exception ex, BatchStrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Cleans up after processing an entity (e.g., detaches from context).
    /// </summary>
    void CleanupEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Creates the final result from tracked successes and failures.
    /// </summary>
    /// <param name="wasCancelled">Whether the operation was cancelled before completing.</param>
    BatchResult<TKey> CreateResult(bool wasCancelled = false);
}

/// <summary>
/// Defines operation-specific behavior for insert batch processing strategies.
/// Unlike IBatchOperation, tracks entities by index since they don't have IDs before insertion.
/// </summary>
internal interface IBatchInsertOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    /// <summary>
    /// Validates all entities before processing. Called once at the start.
    /// </summary>
    void ValidateAll(List<TEntity> entities, BatchStrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Prepares a single entity for the insert operation.
    /// </summary>
    void PrepareEntity(TEntity entity, int index, BatchStrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Records a successful insert after SaveChanges succeeds.
    /// </summary>
    void RecordSuccess(TEntity entity, int index, BatchStrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Records a failed insert after SaveChanges fails.
    /// </summary>
    void RecordFailure(TEntity entity, int index, Exception ex, BatchStrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Cleans up after processing an entity (e.g., detaches from context).
    /// </summary>
    void CleanupEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context);

    /// <summary>
    /// Creates the final result from tracked successes and failures.
    /// </summary>
    /// <param name="wasCancelled">Whether the operation was cancelled before completing.</param>
    InsertBatchResult<TKey> CreateResult(bool wasCancelled = false);
}
