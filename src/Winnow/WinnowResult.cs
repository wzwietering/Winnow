using Winnow.Internal;

namespace Winnow;

/// <summary>
/// Result of an update or delete batch, listing successful entity IDs and per-entity failures.
/// </summary>
/// <remarks>
/// When <see cref="WinnowResultBase{TKey}.ResultDetail"/> is reduced from the
/// default <see cref="ResultDetail.Full"/>, several properties throw
/// <see cref="InvalidOperationException"/> on access:
/// <list type="bullet">
///   <item>At <see cref="ResultDetail.None"/>: <see cref="SuccessfulIds"/>,
///   <see cref="Failures"/>, <see cref="FailedIds"/>,
///   <see cref="WinnowResultBase{TKey}.GraphHierarchy"/>,
///   <see cref="WinnowResultBase{TKey}.TraversalInfo"/>.</item>
///   <item>At <see cref="ResultDetail.Minimal"/>: only the graph properties throw.</item>
/// </list>
/// <see cref="WinnowResultBase{TKey}.SuccessCount"/>,
/// <see cref="WinnowResultBase{TKey}.FailureCount"/>,
/// <see cref="WinnowResultBase{TKey}.Duration"/>, and
/// <see cref="WinnowResultBase{TKey}.WasCancelled"/> are always available.
/// </remarks>
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

    /// <summary>
    /// Structured per-property errors recorded by Winnow pre-validation.
    /// Populated when <see cref="Reason"/> is
    /// <see cref="FailureReason.PreValidationError"/>; <c>null</c> for every
    /// other reason — including <see cref="FailureReason.ValidationError"/>
    /// failures originating from EF Core's save-time validation, which does not
    /// produce structured errors. Use this to drive structured UI / API
    /// responses instead of parsing <see cref="ErrorMessage"/>.
    /// </summary>
    public IReadOnlyList<ValidationError>? ValidationErrors { get; init; }
}

/// <summary>
/// Classifies the reason a batch entity operation failed.
/// </summary>
public enum FailureReason
{
    /// <summary>
    /// Entity rejected by EF Core's save-time validation (e.g. unmapped property,
    /// model mismatch, or other <see cref="InvalidOperationException"/> from
    /// <c>SaveChanges</c>). The <c>ValidationErrors</c> list on the corresponding
    /// failure is always <c>null</c> for this reason — only
    /// <see cref="WinnowFailure{TKey}.ErrorMessage"/> and
    /// <see cref="WinnowFailure{TKey}.Exception"/> describe the issue. For
    /// Winnow's structured pre-validation failures (with per-property
    /// <see cref="ValidationError"/>s), see <see cref="PreValidationError"/>.
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
    UnknownError,

    /// <summary>
    /// Under <c>MatchBy</c> with <c>DuplicateKeyStrategy.RetryAsUpdate</c>, a concurrent
    /// process won a race: a row matching the business key existed long enough to cause
    /// our INSERT to fail, but was gone (or never existed under the configured key) by
    /// the time we re-queried for the retry. The entity has not been persisted.
    /// </summary>
    /// <remarks>
    /// Typical cause is an INSERT-then-DELETE between our save and our retry, but the same
    /// classification fires whenever the duplicate-key retry refresh finds no matching row.
    /// To recover: inspect <see cref="UpsertFailure{TKey}.EntityIndex"/>, decide whether to
    /// discard or re-queue the entity, or wrap the operation in application-level retry
    /// with a delay. A unique constraint on the MatchBy columns narrows but does not
    /// eliminate the race window — the row may be genuinely gone.
    /// </remarks>
    BusinessKeyConflictLost,

    /// <summary>
    /// Entity rejected by Winnow pre-validation — either a configured
    /// <see cref="WinnowValidator{TEntity}"/> delegate or the DataAnnotations
    /// adapter — before any database round trip. The corresponding failure's
    /// <c>ValidationErrors</c> property carries the structured per-property
    /// errors; drive structured UI / API responses off it rather than parsing
    /// <c>ErrorMessage</c>.
    /// </summary>
    PreValidationError
}
