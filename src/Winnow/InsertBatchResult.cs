namespace Winnow;

/// <summary>
/// Result of a batch insert operation. Tracks inserted entities by their
/// original index since entities don't have IDs before insertion.
/// </summary>
public class InsertBatchResult<TKey> : BatchResultBase<TKey> where TKey : notnull, IEquatable<TKey>
{
    private IReadOnlyList<TKey>? _insertedIds;

    /// <summary>
    /// Entities that were successfully inserted with their generated IDs.
    /// </summary>
    public IReadOnlyList<InsertedEntity<TKey>> InsertedEntities { get; init; } = [];

    /// <summary>
    /// Database-generated IDs of all successfully inserted entities.
    /// </summary>
    public IReadOnlyList<TKey> InsertedIds =>
        _insertedIds ??= InsertedEntities.Select(e => e.Id).ToList();

    /// <inheritdoc />
    public override int SuccessCount => InsertedEntities.Count;

    /// <summary>
    /// Details of each failed insert operation.
    /// </summary>
    public IReadOnlyList<InsertBatchFailure> Failures { get; init; } = [];

    /// <inheritdoc />
    public override int FailureCount => Failures.Count;
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
