namespace Winnow;

/// <summary>
/// Options for graph upsert operations (parent + children).
/// </summary>
public class UpsertGraphBatchOptions : GraphBatchOptionsBase
{
    /// <summary>
    /// How to handle children removed from collections.
    /// Default: Throw (safest - user must explicitly choose Delete or Detach).
    /// </summary>
    public OrphanBehavior OrphanedChildBehavior { get; set; } = OrphanBehavior.Throw;

    /// <summary>
    /// How to handle related entities in many-to-many navigations.
    /// Only applies when IncludeManyToMany is true.
    /// Default: AttachExisting.
    /// </summary>
    public ManyToManyInsertBehavior ManyToManyInsertBehavior { get; set; }
        = ManyToManyInsertBehavior.AttachExisting;

    /// <summary>
    /// When true, validates that related many-to-many entities exist in the database
    /// before creating join records. Prevents FK constraint violations at save time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This validation only applies when <see cref="ManyToManyInsertBehavior"/> is set to
    /// <see cref="ManyToManyInsertBehavior.AttachExisting"/>. When using
    /// <see cref="ManyToManyInsertBehavior.InsertIfNew"/>, entities with default keys
    /// are inserted rather than validated for existence.
    /// </para>
    /// <para>
    /// Validation performs batched database queries using AsNoTracking, which does not
    /// affect the change tracker. If any referenced entities are missing, an
    /// <see cref="InvalidOperationException"/> is thrown during processing.
    /// </para>
    /// </remarks>
    /// <value>Default: <c>true</c> (safer).</value>
    public bool ValidateManyToManyEntitiesExist { get; set; } = true;

    /// <summary>
    /// When true, throws an exception if many-to-many validation cannot be performed
    /// for entities with composite primary keys.
    /// When false, validation is silently skipped for composite key entities.
    /// Default: false (backwards compatible).
    /// </summary>
    /// <remarks>
    /// Many-to-many validation currently requires single-column primary keys.
    /// For entities with composite keys, the query service cannot perform
    /// existence validation. Set this to true to be explicitly notified
    /// when validation is skipped, ensuring you're aware of potential
    /// FK constraint violations at save time.
    /// </remarks>
    public bool ThrowOnUnsupportedValidation { get; set; } = false;

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
