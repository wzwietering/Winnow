using Winnow.Internal;

namespace Winnow;

/// <summary>
/// Entity to report succeeded and failed CRUD entities
/// </summary>
public class WinnowResult<TKey> : WinnowResultBase<TKey> where TKey : notnull, IEquatable<TKey>
{
    private readonly IReadOnlyList<TKey> _successfulIds = [];
    private readonly IReadOnlyList<WinnowFailure<TKey>> _failures = [];
    private IReadOnlyList<TKey>? _failedIdsCache;

    /// <summary>
    /// IDs of entities that were successfully processed. Throws when
    /// <see cref="WinnowResultBase{TKey}.ResultDetail"/> is lower than
    /// <see cref="ResultDetail.Minimal"/>.
    /// </summary>
    public IReadOnlyList<TKey> SuccessfulIds
    {
        get => ResultDetail >= ResultDetail.Minimal
            ? _successfulIds
            : throw ResultDetailGuard.NotCaptured(nameof(SuccessfulIds), ResultDetail.Minimal, ResultDetail);
        init => _successfulIds = value ?? [];
    }

    internal IReadOnlyList<TKey> SuccessfulIdsRaw => _successfulIds;

    /// <summary>
    /// Details of each failed entity operation. Throws when
    /// <see cref="WinnowResultBase{TKey}.ResultDetail"/> is lower than
    /// <see cref="ResultDetail.Minimal"/>. At <see cref="ResultDetail.Minimal"/>
    /// the <see cref="WinnowFailure{TKey}.Exception"/> is null.
    /// </summary>
    public IReadOnlyList<WinnowFailure<TKey>> Failures
    {
        get => ResultDetail >= ResultDetail.Minimal
            ? _failures
            : throw ResultDetailGuard.NotCaptured(nameof(Failures), ResultDetail.Minimal, ResultDetail);
        init => _failures = value ?? [];
    }

    internal IReadOnlyList<WinnowFailure<TKey>> FailuresRaw => _failures;

    /// <summary>
    /// IDs of entities that failed processing. Throws when
    /// <see cref="WinnowResultBase{TKey}.ResultDetail"/> is lower than
    /// <see cref="ResultDetail.Minimal"/>.
    /// </summary>
    public IReadOnlyList<TKey> FailedIds
    {
        get
        {
            if (ResultDetail < ResultDetail.Minimal)
                throw ResultDetailGuard.NotCaptured(nameof(FailedIds), ResultDetail.Minimal, ResultDetail);
            return _failedIdsCache ??= _failures.Select(f => f.EntityId).ToList();
        }
    }

    /// <inheritdoc />
    protected override int GetCollectionSuccessCount() => _successfulIds.Count;

    /// <inheritdoc />
    protected override int GetCollectionFailureCount() => _failures.Count;
}

/// <summary>
/// Details of a single entity failure within a batch operation.
/// </summary>
public class WinnowFailure<TKey> where TKey : notnull, IEquatable<TKey>
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
    /// The original exception, if available. Null when ResultDetail is Minimal.
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
