namespace Winnow;

/// <summary>
/// Controls how much per-entity detail batch operations capture in their results.
/// Higher detail levels increase memory allocation; lower levels disable
/// reporting-only collections while preserving correctness of the operation itself.
/// </summary>
/// <remarks>
/// <para>
/// Reducing detail does not change which rows are written, only what the result
/// object reports. <see cref="WinnowResultBase{TKey}.SuccessCount"/> and
/// <see cref="WinnowResultBase{TKey}.FailureCount"/> remain accurate at every level.
/// </para>
/// <para>
/// Properties whose backing data was not captured throw
/// <see cref="InvalidOperationException"/> on access (rather than returning
/// silently empty collections). The exception message names the always-available
/// alternative.
/// </para>
/// <para>
/// Numeric ordering is meaningful: higher values capture more detail. Use
/// <c>detail &gt;= ResultDetail.Minimal</c> to test whether per-entity IDs
/// are tracked.
/// </para>
/// </remarks>
public enum ResultDetail
{
    /// <summary>
    /// Captures only aggregate counts (<see cref="WinnowResultBase{TKey}.SuccessCount"/>,
    /// <see cref="WinnowResultBase{TKey}.FailureCount"/>, <see cref="WinnowResultBase{TKey}.TotalRetries"/>,
    /// <see cref="WinnowResultBase{TKey}.Duration"/>). No per-entity data. Lowest memory cost.
    /// </summary>
    None = 0,

    /// <summary>
    /// Captures successful IDs and failure indices with error messages,
    /// but drops entity object references, exception object references,
    /// graph hierarchy, and traversal statistics. Substantially reduces
    /// memory for typical batches.
    /// </summary>
    Minimal = 1,

    /// <summary>
    /// Default. Captures inserted entity references, failure exceptions,
    /// graph hierarchy, and traversal statistics. Highest memory cost.
    /// </summary>
    Full = 2,
}
