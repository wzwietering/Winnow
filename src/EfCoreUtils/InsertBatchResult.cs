namespace EfCoreUtils;

/// <summary>
/// Result of a batch insert operation. Tracks inserted entities by their
/// original index since entities don't have IDs before insertion.
/// </summary>
public class InsertBatchResult<TKey> where TKey : notnull, IEquatable<TKey>
{
    public IReadOnlyList<InsertedEntity<TKey>> InsertedEntities { get; init; } = [];
    public IReadOnlyList<TKey> InsertedIds => InsertedEntities.Select(e => e.Id).ToList();
    public int SuccessCount => InsertedEntities.Count;

    public IReadOnlyList<InsertBatchFailure> Failures { get; init; } = [];
    public int FailureCount => Failures.Count;

    public int TotalProcessed => SuccessCount + FailureCount;
    public double SuccessRate => TotalProcessed > 0 ? (double)SuccessCount / TotalProcessed : 0;

    public TimeSpan Duration { get; init; }
    public int DatabaseRoundTrips { get; init; }

    /// <summary>
    /// For graph inserts only: Full hierarchy of inserted entities.
    /// Null for parent-only InsertBatch operations.
    /// </summary>
    public IReadOnlyList<GraphNode<TKey>>? GraphHierarchy { get; init; }

    /// <summary>
    /// For graph inserts only: Statistics about the traversal.
    /// Null for parent-only InsertBatch operations.
    /// </summary>
    public GraphTraversalResult<TKey>? TraversalInfo { get; init; }

    public bool IsCompleteSuccess => FailureCount == 0 && SuccessCount > 0;
    public bool IsCompleteFailure => SuccessCount == 0 && FailureCount > 0;
    public bool IsPartialSuccess => SuccessCount > 0 && FailureCount > 0;
}

/// <summary>
/// Represents a successfully inserted entity with its generated ID.
/// </summary>
public class InsertedEntity<TKey> where TKey : notnull, IEquatable<TKey>
{
    /// <summary>
    /// The database-generated ID after insertion.
    /// </summary>
    public TKey Id { get; init; } = default!;

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
/// Represents a failed insert operation.
/// </summary>
public class InsertBatchFailure
{
    /// <summary>
    /// Position in the original input collection.
    /// </summary>
    public int EntityIndex { get; init; }

    public string ErrorMessage { get; init; } = string.Empty;
    public FailureReason Reason { get; init; }
    public Exception? Exception { get; init; }
}
