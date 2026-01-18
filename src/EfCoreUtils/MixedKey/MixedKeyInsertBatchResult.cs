namespace EfCoreUtils.MixedKey;

/// <summary>
/// Represents a successfully inserted entity with its generated ID.
/// </summary>
public class MixedKeyInsertedEntity
{
    /// <summary>
    /// The database-generated ID after insertion.
    /// </summary>
    public MixedKeyId Id { get; init; }

    /// <summary>
    /// Position in the original input collection.
    /// </summary>
    public int OriginalIndex { get; init; }

    /// <summary>
    /// Reference to the entity (now has ID populated).
    /// </summary>
    public object Entity { get; init; } = null!;
}

/// <summary>
/// Result of a batch insert operation with mixed key types.
/// Tracks inserted entities by their original index since entities don't have IDs before insertion.
/// </summary>
public class MixedKeyInsertBatchResult : IBatchResult
{
    /// <summary>
    /// Entities that were successfully inserted.
    /// </summary>
    public IReadOnlyList<MixedKeyInsertedEntity> InsertedEntities { get; init; } = [];

    /// <summary>
    /// Details of entities that failed insertion.
    /// </summary>
    public IReadOnlyList<InsertBatchFailure> Failures { get; init; } = [];

    /// <summary>
    /// Total time taken for the operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Number of database round trips made.
    /// </summary>
    public int DatabaseRoundTrips { get; init; }

    /// <summary>
    /// IDs of entities that were successfully inserted.
    /// </summary>
    public IReadOnlyList<MixedKeyId> InsertedIds => InsertedEntities.Select(e => e.Id).ToList();

    /// <summary>
    /// Number of entities successfully inserted.
    /// </summary>
    public int SuccessCount => InsertedEntities.Count;

    /// <summary>
    /// Number of entities that failed insertion.
    /// </summary>
    public int FailureCount => Failures.Count;

    /// <summary>
    /// Total number of entities processed (success + failure).
    /// </summary>
    public int TotalProcessed => SuccessCount + FailureCount;

    /// <summary>
    /// Ratio of successful operations (0.0 to 1.0).
    /// </summary>
    public double SuccessRate => TotalProcessed > 0 ? (double)SuccessCount / TotalProcessed : 0;

    /// <summary>
    /// True if all entities were inserted successfully.
    /// </summary>
    public bool IsCompleteSuccess => FailureCount == 0 && SuccessCount > 0;

    /// <summary>
    /// True if all entities failed insertion.
    /// </summary>
    public bool IsCompleteFailure => SuccessCount == 0 && FailureCount > 0;

    /// <summary>
    /// True if some entities succeeded and some failed.
    /// </summary>
    public bool IsPartialSuccess => SuccessCount > 0 && FailureCount > 0;

    /// <summary>
    /// For graph inserts only: Full hierarchy of inserted entities.
    /// Null for parent-only operations.
    /// </summary>
    public IReadOnlyList<MixedKeyGraphNode>? GraphHierarchy { get; init; }

    /// <summary>
    /// For graph inserts only: Statistics about the traversal.
    /// Null for parent-only operations.
    /// </summary>
    public MixedKeyGraphTraversalResult? TraversalInfo { get; init; }
}
