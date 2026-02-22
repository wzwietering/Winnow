namespace Winnow;

/// <summary>
/// Strategy for handling duplicate key errors during upsert INSERT attempts.
/// </summary>
/// <remarks>
/// <para><strong>Race Condition Context:</strong></para>
/// <para>
/// When multiple processes attempt to insert entities with the same key simultaneously,
/// one will succeed and others may fail with a duplicate key error. This strategy
/// determines how those failures are handled.
/// </para>
/// </remarks>
public enum DuplicateKeyStrategy
{
    /// <summary>
    /// Fail and record in Failures collection (default).
    /// </summary>
    Fail = 0,

    /// <summary>
    /// Retry the failed INSERT as an UPDATE.
    /// Handles race conditions where another process inserted the same key.
    /// </summary>
    RetryAsUpdate,

    /// <summary>
    /// Skip silently without recording as failure.
    /// Useful for "insert if not exists" semantics.
    /// </summary>
    Skip
}
