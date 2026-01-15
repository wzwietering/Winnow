namespace EfCoreUtils;

/// <summary>
/// Defines operation-specific behavior for batch processing strategies.
/// Strategies (OneByOne, DivideAndConquer) use this interface to delegate
/// operation-specific logic (Update, Insert, Delete) while reusing the algorithm.
/// </summary>
internal interface IBatchOperation<TEntity> where TEntity : class
{
    /// <summary>
    /// Validates all entities before processing. Called once at the start.
    /// </summary>
    void ValidateAll(List<TEntity> entities, BatchStrategyContext<TEntity> context);

    /// <summary>
    /// Prepares a single entity for the database operation (e.g., sets entity state).
    /// </summary>
    void PrepareEntity(TEntity entity, BatchStrategyContext<TEntity> context);

    /// <summary>
    /// Records a successful operation after SaveChanges succeeds.
    /// </summary>
    void RecordSuccess(TEntity entity, BatchStrategyContext<TEntity> context);

    /// <summary>
    /// Records a failed operation after SaveChanges fails.
    /// </summary>
    void RecordFailure(TEntity entity, Exception ex, BatchStrategyContext<TEntity> context);

    /// <summary>
    /// Cleans up after processing an entity (e.g., detaches from context).
    /// </summary>
    void CleanupEntity(TEntity entity, BatchStrategyContext<TEntity> context);

    /// <summary>
    /// Creates the final result from tracked successes and failures.
    /// </summary>
    BatchResult CreateResult();
}
