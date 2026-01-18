namespace EfCoreUtils.MixedKey;

/// <summary>
/// Result of a batch update or delete operation with mixed key types.
/// </summary>
public class MixedKeyBatchResult : IBatchResult
{
    /// <summary>
    /// IDs of entities that were successfully processed.
    /// </summary>
    public IReadOnlyList<MixedKeyId> SuccessfulIds { get; init; } = [];

    /// <summary>
    /// Details of entities that failed processing.
    /// </summary>
    public IReadOnlyList<MixedKeyBatchFailure> Failures { get; init; } = [];

    /// <summary>
    /// Total time taken for the operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Number of database round trips made.
    /// </summary>
    public int DatabaseRoundTrips { get; init; }

    /// <summary>
    /// Number of entities successfully processed.
    /// </summary>
    public int SuccessCount => SuccessfulIds.Count;

    /// <summary>
    /// IDs of entities that failed processing.
    /// </summary>
    public IReadOnlyList<MixedKeyId> FailedIds => Failures.Select(f => f.EntityId).ToList();

    /// <summary>
    /// Number of entities that failed processing.
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
    /// True if all entities were processed successfully.
    /// </summary>
    public bool IsCompleteSuccess => FailureCount == 0 && SuccessCount > 0;

    /// <summary>
    /// True if all entities failed processing.
    /// </summary>
    public bool IsCompleteFailure => SuccessCount == 0 && FailureCount > 0;

    /// <summary>
    /// True if some entities succeeded and some failed.
    /// </summary>
    public bool IsPartialSuccess => SuccessCount > 0 && FailureCount > 0;

    /// <summary>
    /// For graph operations only: Full hierarchy of processed entities.
    /// Null for parent-only operations.
    /// </summary>
    public IReadOnlyList<MixedKeyGraphNode>? GraphHierarchy { get; init; }

    /// <summary>
    /// For graph operations only: Statistics about the traversal.
    /// Null for parent-only operations.
    /// </summary>
    public MixedKeyGraphTraversalResult? TraversalInfo { get; init; }
}
