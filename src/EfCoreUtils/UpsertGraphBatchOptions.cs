namespace EfCoreUtils;

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
}
