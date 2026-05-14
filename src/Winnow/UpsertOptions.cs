using System.Linq.Expressions;

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
    /// Optional business-key expression used to look up existing rows in the database
    /// instead of relying on primary-key default-value detection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Supports a single property (<c>e =&gt; e.ExternalId</c>) or an anonymous projection
    /// (<c>e =&gt; new { e.TenantId, e.ExternalId }</c>) for composite match keys.
    /// </para>
    /// <para>
    /// When set, upsert performs a single batched SELECT before SaveChanges to partition
    /// entities into insert/update sets by business key. The resolved primary key replaces
    /// the input entity's PK on update. Null preserves legacy <c>HasDefaultKeyValue</c> behavior.
    /// </para>
    /// <para>
    /// Assign via <see cref="UpsertOptionsExtensions.WithMatchBy{TEntity, TKey}"/> — the setter
    /// is internal so the fluent helper is the only path, guaranteeing shape validation runs
    /// at configuration time. Graph upsert (<c>UpsertGraph</c>) does not yet support MatchBy.
    /// </para>
    /// </remarks>
    public LambdaExpression? MatchBy { get; internal set; }
}
