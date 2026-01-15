namespace EfCoreUtils;

/// <summary>
/// Options for graph insert batch operations (parent + children).
/// </summary>
public class InsertGraphBatchOptions
{
    public BatchStrategy Strategy { get; set; } = BatchStrategy.OneByOne;
}
