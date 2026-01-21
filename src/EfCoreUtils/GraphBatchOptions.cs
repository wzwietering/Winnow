namespace EfCoreUtils;

/// <summary>
/// Options for graph batch update operations.
/// </summary>
public class GraphBatchOptions
{
    /// <summary>
    /// Strategy to use for batch processing. Default: OneByOne (safer for graphs).
    /// </summary>
    public BatchStrategy Strategy { get; set; } = BatchStrategy.OneByOne;

    /// <summary>
    /// How to handle children removed from collections.
    /// Default: Throw (safest - user must explicitly choose Delete or Detach).
    /// </summary>
    public OrphanBehavior OrphanedChildBehavior { get; set; } = OrphanBehavior.Throw;

    /// <summary>
    /// Maximum depth to traverse in the entity graph.
    /// Default: 10. Use to prevent infinite recursion in deep hierarchies.
    /// </summary>
    public int MaxDepth { get; set; } = 10;

    /// <summary>
    /// When true, includes reference navigations (many-to-one) during traversal.
    /// Example: When updating OrderItem, also update its Product reference.
    /// Both entity and referenced entities are updated atomically.
    /// Traversal depth controlled by MaxDepth. Default: false.
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
    /// Join records will be managed (created/removed) based on collection changes.
    /// Only the traversed direction is processed - inverse navigation not auto-followed.
    /// Default: false.
    /// </summary>
    public bool IncludeManyToMany { get; set; } = false;
}
