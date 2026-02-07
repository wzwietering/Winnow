namespace EfCoreUtils;

/// <summary>
/// Result of a batch insert operation. Tracks inserted entities by their
/// original index since entities don't have IDs before insertion.
/// </summary>
public class InsertBatchResult<TKey> : BatchResultBase<TKey> where TKey : notnull, IEquatable<TKey>
{
    private IReadOnlyList<TKey>? _insertedIds;

    public IReadOnlyList<InsertedEntity<TKey>> InsertedEntities { get; init; } = [];

    public IReadOnlyList<TKey> InsertedIds =>
        _insertedIds ??= InsertedEntities.Select(e => e.Id).ToList();
    public override int SuccessCount => InsertedEntities.Count;

    public IReadOnlyList<InsertBatchFailure> Failures { get; init; } = [];
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

    public string ErrorMessage { get; init; } = string.Empty;
    public FailureReason Reason { get; init; }
    public Exception? Exception { get; init; }
}
