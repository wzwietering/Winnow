namespace EfCoreUtils;

/// <summary>
/// Options for graph batch update operations.
/// </summary>
public class GraphBatchOptions : GraphBatchOptionsBase
{
    /// <summary>
    /// How to handle children removed from collections.
    /// Default: Throw (safest - user must explicitly choose Delete or Detach).
    /// </summary>
    public OrphanBehavior OrphanedChildBehavior { get; set; } = OrphanBehavior.Throw;

    /// <summary>
    /// Maximum allowed size for many-to-many collections.
    /// Throws if a collection exceeds this size.
    /// Default: 0 (no limit). Set to a positive value to enable.
    /// </summary>
    public int MaxManyToManyCollectionSize { get; set; } = 0;
}
