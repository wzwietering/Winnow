namespace Winnow;

/// <summary>
/// Options for parent-only upsert batch operations.
/// </summary>
public class UpsertBatchOptions : BatchOptions
{
    /// <summary>
    /// How to handle duplicate key errors during INSERT attempts.
    /// Default: Fail.
    /// </summary>
    /// <remarks>
    /// <para><strong>Race Condition Mitigation:</strong></para>
    /// <para>
    /// Set to <see cref="DuplicateKeyStrategy.RetryAsUpdate"/> to automatically
    /// retry failed inserts as updates. This handles the case where another process
    /// inserts the same key between key detection and SaveChanges.
    /// </para>
    /// </remarks>
    public DuplicateKeyStrategy DuplicateKeyStrategy { get; set; } = DuplicateKeyStrategy.Fail;
}
