namespace EfCoreUtils;

/// <summary>
/// Options for graph upsert operations (parent + children).
/// </summary>
public class UpsertGraphBatchOptions
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
    /// How to handle children removed from collections.
    /// Default: Throw (safest - user must explicitly choose Delete or Detach).
    /// </summary>
    public OrphanBehavior OrphanedChildBehavior { get; set; } = OrphanBehavior.Throw;

    /// <summary>
    /// When true, includes reference navigations (many-to-one) during traversal.
    /// Referenced entities will be upserted if they are new or updated.
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
}
