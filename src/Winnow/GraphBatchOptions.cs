namespace Winnow;

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

}
