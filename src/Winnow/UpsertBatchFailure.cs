namespace Winnow;

/// <summary>
/// Represents a failed upsert operation.
/// </summary>
public class UpsertBatchFailure<TKey> where TKey : notnull, IEquatable<TKey>
{
    /// <summary>
    /// Position in the original input collection.
    /// </summary>
    public int EntityIndex { get; init; }

    /// <summary>
    /// Entity ID if known (null for entities with default keys that failed during insert).
    /// </summary>
    public TKey? EntityId { get; init; }

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

    /// <summary>
    /// The operation that was attempted when failure occurred.
    /// </summary>
    public UpsertOperationType AttemptedOperation { get; init; }

    /// <summary>
    /// True if the entity had a default key when the operation was attempted.
    /// When true, EntityId will be default(TKey) and EntityIndex should be used to identify the entity.
    /// </summary>
    public bool IsDefaultKey { get; init; }
}
