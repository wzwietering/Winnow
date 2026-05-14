using Winnow.Internal;

namespace Winnow;

/// <summary>
/// Options for parent-only upsert batch operations.
/// </summary>
public class UpsertOptions : WinnowOptions
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

    /// <summary>
    /// Internal carrier for a configured MatchBy expression. The public path is
    /// <see cref="UpsertOptionsExtensions.WithMatchBy{TEntity, TKey}"/> (or the
    /// single-type-argument overload), which performs shape validation before
    /// storing the expression here. Wrapped so future configuration fields can
    /// be added without breaking the public API. Graph upsert does not yet
    /// support MatchBy.
    /// </summary>
    internal MatchByConfiguration? MatchBy { get; set; }
}
