namespace EfCoreUtils;

/// <summary>
/// Entity to report succeeded and failed CRUD entities
/// </summary>
public class BatchResult
{
    public IReadOnlyList<int> SuccessfulIds { get; init; } = [];
    public int SuccessCount => SuccessfulIds.Count;

    public IReadOnlyList<BatchFailure> Failures { get; init; } = [];
    public IReadOnlyList<int> FailedIds => Failures.Select(f => f.EntityId).ToList();
    public int FailureCount => Failures.Count;

    public int TotalProcessed => SuccessCount + FailureCount;
    public double SuccessRate => TotalProcessed > 0 ? (double)SuccessCount / TotalProcessed : 0;

    public TimeSpan Duration { get; init; }
    public int DatabaseRoundTrips { get; init; }

    public bool IsCompleteSuccess => FailureCount == 0 && SuccessCount > 0;
    public bool IsCompleteFailure => SuccessCount == 0 && FailureCount > 0;
    public bool IsPartialSuccess => SuccessCount > 0 && FailureCount > 0;
}

public class BatchFailure
{
    public int EntityId { get; init; }
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
