namespace EfCoreUtils;

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

    public string ErrorMessage { get; init; } = string.Empty;
    public FailureReason Reason { get; init; }
    public Exception? Exception { get; init; }

    /// <summary>
    /// The operation that was attempted when failure occurred.
    /// </summary>
    public UpsertOperation AttemptedOperation { get; init; }
}
