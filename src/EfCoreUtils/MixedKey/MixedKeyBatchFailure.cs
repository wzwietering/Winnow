namespace EfCoreUtils.MixedKey;

/// <summary>
/// Represents a failed operation for an entity with a mixed key type.
/// </summary>
public class MixedKeyBatchFailure
{
    /// <summary>
    /// The ID of the entity that failed.
    /// </summary>
    public MixedKeyId EntityId { get; init; }

    /// <summary>
    /// Description of why the operation failed.
    /// </summary>
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>
    /// Categorized reason for the failure.
    /// </summary>
    public FailureReason Reason { get; init; }

    /// <summary>
    /// The exception that caused the failure, if any.
    /// </summary>
    public Exception? Exception { get; init; }
}
