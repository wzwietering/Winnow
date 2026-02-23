namespace Winnow;

/// <summary>
/// Options for parent-only batch operations.
/// </summary>
public class WinnowOptions
{
    /// <summary>
    /// Strategy to use for batch processing. Default: OneByOne.
    /// </summary>
    public BatchStrategy Strategy { get; set; } = BatchStrategy.OneByOne;

    /// <summary>
    /// When true (default), validates that navigation properties are not modified.
    /// Set to false to allow navigation properties to be loaded but ignored.
    /// </summary>
    public bool ValidateNavigationProperties { get; set; } = true;

    /// <summary>
    /// When set, enables automatic retry with exponential backoff for transient failures.
    /// </summary>
    public RetryOptions? Retry { get; set; }
}
