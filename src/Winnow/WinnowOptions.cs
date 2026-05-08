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

    /// <summary>
    /// Controls how much per-entity detail the result captures. Default: <see cref="Winnow.ResultDetail.Full"/>.
    /// Lower levels (<see cref="Winnow.ResultDetail.Minimal"/>, <see cref="Winnow.ResultDetail.None"/>) reduce memory at the cost
    /// of dropping reporting-only collections. <see cref="WinnowResultBase{TKey}.SuccessCount"/> and
    /// <see cref="WinnowResultBase{TKey}.FailureCount"/> remain accurate at every level.
    /// </summary>
    public ResultDetail ResultDetail { get; set; } = ResultDetail.Full;
}
