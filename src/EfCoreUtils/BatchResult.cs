namespace EfCoreUtils;

/// <summary>
/// Entity to report succeeded and failed CRUD entities
/// </summary>
public class BatchResult<TKey> : BatchResultBase<TKey> where TKey : notnull, IEquatable<TKey>
{
    public IReadOnlyList<TKey> SuccessfulIds { get; init; } = [];
    public override int SuccessCount => SuccessfulIds.Count;

    public IReadOnlyList<BatchFailure<TKey>> Failures { get; init; } = [];
    public IReadOnlyList<TKey> FailedIds => Failures.Select(f => f.EntityId).ToList();
    public override int FailureCount => Failures.Count;
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
    DuplicateKey,
    Cancelled,
    UnknownError
}
