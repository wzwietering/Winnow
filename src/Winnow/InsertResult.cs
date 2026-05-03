using Winnow.Internal;

namespace Winnow;

/// <summary>
/// Result of a batch insert operation. Tracks inserted entities by their
/// original index since entities don't have IDs before insertion.
/// </summary>
public class InsertResult<TKey> : WinnowResultBase<TKey> where TKey : notnull, IEquatable<TKey>
{
    private readonly IReadOnlyList<InsertedEntity<TKey>> _insertedEntities = [];
    private readonly IReadOnlyList<TKey> _explicitInsertedIds = [];
    private readonly IReadOnlyList<InsertFailure> _failures = [];
    private IReadOnlyList<TKey>? _insertedIdsCache;

    /// <summary>
    /// Entities that were successfully inserted with their generated IDs.
    /// Throws when <see cref="WinnowResultBase{TKey}.ResultDetail"/> is lower
    /// than <see cref="ResultDetail.Full"/>.
    /// </summary>
    public IReadOnlyList<InsertedEntity<TKey>> InsertedEntities
    {
        get => ResultDetail >= ResultDetail.Full
            ? _insertedEntities
            : throw ResultDetailGuard.NotCaptured(
                nameof(InsertedEntities), ResultDetail.Full, ResultDetail, $"{nameof(InsertedIds)}");
        init => _insertedEntities = value ?? [];
    }

    internal IReadOnlyList<InsertedEntity<TKey>> InsertedEntitiesRaw => _insertedEntities;

    /// <summary>
    /// Database-generated IDs of all successfully inserted entities. Throws
    /// when <see cref="WinnowResultBase{TKey}.ResultDetail"/> is lower than
    /// <see cref="ResultDetail.Minimal"/>.
    /// </summary>
    /// <remarks>
    /// At <see cref="ResultDetail.Full"/>, the IDs are projected from
    /// <see cref="InsertedEntities"/>. At <see cref="ResultDetail.Minimal"/>,
    /// they are tracked directly.
    /// </remarks>
    public IReadOnlyList<TKey> InsertedIds
    {
        get
        {
            if (ResultDetail < ResultDetail.Minimal)
                throw ResultDetailGuard.NotCaptured(nameof(InsertedIds), ResultDetail.Minimal, ResultDetail);
            return _insertedIdsCache ??= _insertedEntities.Count > 0
                ? _insertedEntities.Select(e => e.Id).ToList()
                : _explicitInsertedIds;
        }
        init => _explicitInsertedIds = value ?? [];
    }

    internal IReadOnlyList<TKey> InsertedIdsRaw => _explicitInsertedIds;

    /// <summary>
    /// Details of each failed insert operation. Throws when
    /// <see cref="WinnowResultBase{TKey}.ResultDetail"/> is lower than
    /// <see cref="ResultDetail.Minimal"/>. At <see cref="ResultDetail.Minimal"/>
    /// the <see cref="InsertFailure.Exception"/> is null.
    /// </summary>
    public IReadOnlyList<InsertFailure> Failures
    {
        get => ResultDetail >= ResultDetail.Minimal
            ? _failures
            : throw ResultDetailGuard.NotCaptured(nameof(Failures), ResultDetail.Minimal, ResultDetail);
        init => _failures = value ?? [];
    }

    internal IReadOnlyList<InsertFailure> FailuresRaw => _failures;

    /// <inheritdoc />
    protected override int GetCollectionSuccessCount() =>
        _insertedEntities.Count > 0 ? _insertedEntities.Count : _explicitInsertedIds.Count;

    /// <inheritdoc />
    protected override int GetCollectionFailureCount() => _failures.Count;
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
public class InsertFailure
{
    /// <summary>
    /// Position in the original input collection.
    /// </summary>
    public int EntityIndex { get; init; }

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
