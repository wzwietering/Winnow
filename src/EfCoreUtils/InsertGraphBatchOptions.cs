namespace EfCoreUtils;

/// <summary>
/// Options for graph insert batch operations (parent + children).
/// </summary>
public class InsertGraphBatchOptions
{
    /// <summary>
    /// Strategy to use for batch processing. Default: OneByOne (safer for graphs).
    /// </summary>
    public BatchStrategy Strategy { get; set; } = BatchStrategy.OneByOne;

    /// <summary>
    /// Maximum depth to traverse in the entity graph.
    /// Default: 10. Use to prevent infinite recursion in deep hierarchies.
    /// </summary>
    public int MaxDepth { get; set; } = 10;

    /// <summary>
    /// When true, includes reference navigations (many-to-one) during traversal.
    /// Referenced entities will be inserted if they are new.
    /// Default: false.
    /// </summary>
    public bool IncludeReferences { get; set; } = false;

    /// <summary>
    /// How to handle circular references during traversal.
    /// Only applies when IncludeReferences is true.
    /// Default: Throw (safest).
    /// </summary>
    public CircularReferenceHandling CircularReferenceHandling { get; set; }
        = CircularReferenceHandling.Throw;

    /// <summary>
    /// When true, includes many-to-many navigations during traversal.
    /// Creates join records for related entities.
    /// Default: false.
    /// </summary>
    public bool IncludeManyToMany { get; set; } = false;

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
    /// Maximum allowed size for many-to-many collections.
    /// Throws if a collection exceeds this size.
    /// Default: 0 (no limit). Set to a positive value to enable.
    /// </summary>
    public int MaxManyToManyCollectionSize { get; set; } = 0;
}
