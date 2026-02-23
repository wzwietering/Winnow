namespace Winnow;

/// <summary>
/// Base class for batch operation results, providing common computed properties.
/// </summary>
public abstract class WinnowResultBase<TKey> where TKey : notnull, IEquatable<TKey>
{
    /// <summary>
    /// Number of successfully processed entities.
    /// </summary>
    public abstract int SuccessCount { get; }

    /// <summary>
    /// Number of failed entities.
    /// </summary>
    public abstract int FailureCount { get; }

    /// <summary>
    /// Total number of entities processed (success + failure).
    /// </summary>
    public int TotalProcessed => SuccessCount + FailureCount;

    /// <summary>
    /// Ratio of successful entities to total processed (0-1).
    /// </summary>
    public double SuccessRate => TotalProcessed > 0 ? (double)SuccessCount / TotalProcessed : 0;

    /// <summary>
    /// Time taken for the batch operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Number of database round trips performed.
    /// </summary>
    public int DatabaseRoundTrips { get; init; }

    /// <summary>
    /// For graph operations only: Full hierarchy of processed entities.
    /// Null for parent-only operations.
    /// </summary>
    public IReadOnlyList<GraphNode<TKey>>? GraphHierarchy { get; init; }

    /// <summary>
    /// For graph operations only: Statistics about the traversal.
    /// Null for parent-only operations.
    /// </summary>
    public GraphTraversalResult<TKey>? TraversalInfo { get; init; }

    /// <summary>
    /// True if all entities succeeded and the operation was not cancelled.
    /// </summary>
    public bool IsCompleteSuccess => FailureCount == 0 && SuccessCount > 0 && !WasCancelled;

    /// <summary>
    /// True if all entities failed.
    /// </summary>
    public bool IsCompleteFailure => SuccessCount == 0 && FailureCount > 0;

    /// <summary>
    /// True if some entities succeeded and some failed.
    /// </summary>
    public bool IsPartialSuccess => SuccessCount > 0 && FailureCount > 0;

    /// <summary>
    /// Indicates whether the operation was cancelled before completing.
    /// When true, some entities may not have been processed.
    /// </summary>
    public bool WasCancelled { get; init; }

    /// <summary>
    /// Total number of transient failure retries across the operation.
    /// Zero when RetryOptions is not configured.
    /// </summary>
    public int TotalRetries { get; init; }
}
