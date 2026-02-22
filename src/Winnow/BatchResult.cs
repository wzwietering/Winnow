namespace Winnow;

/// <summary>
/// Entity to report succeeded and failed CRUD entities
/// </summary>
public class BatchResult<TKey> : BatchResultBase<TKey> where TKey : notnull, IEquatable<TKey>
{
    private IReadOnlyList<TKey>? _failedIds;

    /// <summary>
    /// IDs of entities that were successfully processed.
    /// </summary>
    public IReadOnlyList<TKey> SuccessfulIds { get; init; } = [];

    /// <inheritdoc />
    public override int SuccessCount => SuccessfulIds.Count;

    /// <summary>
    /// Details of each failed entity operation.
    /// </summary>
    public IReadOnlyList<BatchFailure<TKey>> Failures { get; init; } = [];

    /// <summary>
    /// IDs of entities that failed processing.
    /// </summary>
    public IReadOnlyList<TKey> FailedIds => _failedIds ??= Failures.Select(f => f.EntityId).ToList();

    /// <inheritdoc />
    public override int FailureCount => Failures.Count;
}

/// <summary>
/// Details of a single entity failure within a batch operation.
/// </summary>
public class BatchFailure<TKey> where TKey : notnull, IEquatable<TKey>
{
    /// <summary>
    /// ID of the entity that failed.
    /// </summary>
    public TKey EntityId { get; init; } = default!;

    /// <summary>
    /// Human-readable description of the failure.
    /// </summary>
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>
    /// Classified reason for the failure.
    /// </summary>
    public FailureReason Reason { get; init; }

    /// <summary>
    /// The original exception, if available.
    /// </summary>
    public Exception? Exception { get; init; }
}

/// <summary>
/// Classifies the reason a batch entity operation failed.
/// </summary>
public enum FailureReason
{
    /// <summary>
    /// Entity failed model or business rule validation.
    /// </summary>
    ValidationError,

    /// <summary>
    /// Optimistic concurrency conflict detected.
    /// </summary>
    ConcurrencyConflict,

    /// <summary>
    /// Database constraint violation (foreign key, unique, check).
    /// </summary>
    DatabaseConstraint,

    /// <summary>
    /// Duplicate primary or unique key detected.
    /// </summary>
    DuplicateKey,

    /// <summary>
    /// Operation was cancelled via CancellationToken.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Failure could not be classified into a known category.
    /// </summary>
    UnknownError
}
