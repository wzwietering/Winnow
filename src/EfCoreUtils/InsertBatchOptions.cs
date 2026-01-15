namespace EfCoreUtils;

/// <summary>
/// Options for parent-only insert batch operations.
/// </summary>
public class InsertBatchOptions
{
    public BatchStrategy Strategy { get; set; } = BatchStrategy.OneByOne;

    /// <summary>
    /// When true (default), throws if navigation properties are populated.
    /// Use InsertGraphBatch to insert parent with children.
    /// </summary>
    public bool ValidateNavigationProperties { get; set; } = true;
}
