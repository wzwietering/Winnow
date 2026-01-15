namespace EfCoreUtils;

/// <summary>
/// Options for parent-only delete operations.
/// </summary>
public class DeleteBatchOptions
{
    /// <summary>
    /// The batch processing strategy to use. Default is OneByOne for maximum failure isolation.
    /// </summary>
    public BatchStrategy Strategy { get; set; } = BatchStrategy.OneByOne;

    /// <summary>
    /// When true (default), throws if navigation properties are populated.
    /// Use DeleteGraphBatch to delete parent with children.
    /// </summary>
    public bool ValidateNavigationProperties { get; set; } = true;
}
