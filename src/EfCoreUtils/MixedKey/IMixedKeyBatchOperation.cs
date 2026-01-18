namespace EfCoreUtils.MixedKey;

/// <summary>
/// Defines operation-specific behavior for mixed-key batch processing strategies.
/// Strategies (OneByOne, DivideAndConquer) use this interface to delegate
/// operation-specific logic (Update, Delete) while reusing the algorithm.
/// </summary>
internal interface IMixedKeyBatchOperation<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Validates all entities before processing. Called once at the start.
    /// </summary>
    void ValidateAll(List<TEntity> entities, MixedKeyBatchStrategyContext<TEntity> context);

    /// <summary>
    /// Prepares a single entity for the database operation (e.g., sets entity state).
    /// </summary>
    void PrepareEntity(TEntity entity, MixedKeyBatchStrategyContext<TEntity> context);

    /// <summary>
    /// Records a successful operation after SaveChanges succeeds.
    /// </summary>
    void RecordSuccess(TEntity entity, MixedKeyBatchStrategyContext<TEntity> context);

    /// <summary>
    /// Records a failed operation after SaveChanges fails.
    /// </summary>
    void RecordFailure(TEntity entity, Exception ex, MixedKeyBatchStrategyContext<TEntity> context);

    /// <summary>
    /// Cleans up after processing an entity (e.g., detaches from context).
    /// </summary>
    void CleanupEntity(TEntity entity, MixedKeyBatchStrategyContext<TEntity> context);

    /// <summary>
    /// Creates the final result from tracked successes and failures.
    /// </summary>
    MixedKeyBatchResult CreateResult();
}

/// <summary>
/// Defines operation-specific behavior for mixed-key insert batch processing strategies.
/// Unlike IMixedKeyBatchOperation, tracks entities by index since they don't have IDs before insertion.
/// </summary>
internal interface IMixedKeyBatchInsertOperation<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Validates all entities before processing. Called once at the start.
    /// </summary>
    void ValidateAll(List<TEntity> entities, MixedKeyBatchStrategyContext<TEntity> context);

    /// <summary>
    /// Prepares a single entity for the insert operation.
    /// </summary>
    void PrepareEntity(TEntity entity, int index, MixedKeyBatchStrategyContext<TEntity> context);

    /// <summary>
    /// Records a successful insert after SaveChanges succeeds.
    /// </summary>
    void RecordSuccess(TEntity entity, int index, MixedKeyBatchStrategyContext<TEntity> context);

    /// <summary>
    /// Records a failed insert after SaveChanges fails.
    /// </summary>
    void RecordFailure(TEntity entity, int index, Exception ex, MixedKeyBatchStrategyContext<TEntity> context);

    /// <summary>
    /// Cleans up after processing an entity (e.g., detaches from context).
    /// </summary>
    void CleanupEntity(TEntity entity, MixedKeyBatchStrategyContext<TEntity> context);

    /// <summary>
    /// Creates the final result from tracked successes and failures.
    /// </summary>
    MixedKeyInsertBatchResult CreateResult();
}
