namespace EfCoreUtils;

/// <summary>
/// Options for graph delete operations (parent + children).
/// </summary>
public class DeleteGraphBatchOptions
{
    /// <summary>
    /// The batch processing strategy to use. Default is OneByOne for maximum failure isolation.
    /// </summary>
    public BatchStrategy Strategy { get; set; } = BatchStrategy.OneByOne;

    /// <summary>
    /// How to handle children when deleting parent. Default is Cascade.
    /// </summary>
    public DeleteCascadeBehavior CascadeBehavior { get; set; } = DeleteCascadeBehavior.Cascade;

    /// <summary>
    /// Maximum depth to traverse in the entity graph.
    /// Default: 10. Use to prevent infinite recursion in deep hierarchies.
    /// </summary>
    public int MaxDepth { get; set; } = 10;
}
