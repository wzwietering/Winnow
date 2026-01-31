namespace EfCoreUtils;

/// <summary>
/// Options for parent-only upsert batch operations.
/// </summary>
public class UpsertBatchOptions
{
    public BatchStrategy Strategy { get; set; } = BatchStrategy.OneByOne;

    /// <summary>
    /// When true (default), throws if navigation properties are populated.
    /// Use UpsertGraphBatch to upsert parent with children.
    /// </summary>
    public bool ValidateNavigationProperties { get; set; } = true;
}
