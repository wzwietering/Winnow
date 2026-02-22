namespace Winnow;

/// <summary>
/// Options for graph delete operations (parent + children).
/// </summary>
public class DeleteGraphBatchOptions : GraphBatchOptionsBase
{
    /// <summary>
    /// How to handle children when deleting parent. Default is Cascade.
    /// </summary>
    public DeleteCascadeBehavior CascadeBehavior { get; set; } = DeleteCascadeBehavior.Cascade;

    /// <summary>
    /// When true, validates that referenced entities exist before deletion.
    /// Prevents FK constraint violations. Default: true (safer).
    /// </summary>
    public bool ValidateReferencedEntitiesExist { get; set; } = true;
}
