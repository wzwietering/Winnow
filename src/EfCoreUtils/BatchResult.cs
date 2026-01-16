namespace EfCoreUtils;

/// <summary>
/// Entity to report succeeded and failed CRUD entities
/// </summary>
public class BatchResult<TKey> where TKey : notnull, IEquatable<TKey>
{
    public IReadOnlyList<TKey> SuccessfulIds { get; init; } = [];
    public int SuccessCount => SuccessfulIds.Count;

    public IReadOnlyList<BatchFailure<TKey>> Failures { get; init; } = [];
    public IReadOnlyList<TKey> FailedIds => Failures.Select(f => f.EntityId).ToList();
    public int FailureCount => Failures.Count;

    public int TotalProcessed => SuccessCount + FailureCount;
    public double SuccessRate => TotalProcessed > 0 ? (double)SuccessCount / TotalProcessed : 0;

    public TimeSpan Duration { get; init; }
    public int DatabaseRoundTrips { get; init; }

    /// <summary>
    /// For graph operations only: Maps each successful parent ID to its child IDs.
    /// Null for parent-only UpdateBatch operations.
    /// </summary>
    public IReadOnlyDictionary<TKey, IReadOnlyList<TKey>>? ChildIdsByParentId { get; init; }

    public bool IsCompleteSuccess => FailureCount == 0 && SuccessCount > 0;
    public bool IsCompleteFailure => SuccessCount == 0 && FailureCount > 0;
    public bool IsPartialSuccess => SuccessCount > 0 && FailureCount > 0;
}

public class BatchFailure<TKey> where TKey : notnull, IEquatable<TKey>
{
    public TKey EntityId { get; init; } = default!;
    public string ErrorMessage { get; init; } = string.Empty;
    public FailureReason Reason { get; init; }
    public Exception? Exception { get; init; }
}

public enum FailureReason
{
    ValidationError,
    ConcurrencyConflict,
    DatabaseConstraint,
    UnknownError
}
