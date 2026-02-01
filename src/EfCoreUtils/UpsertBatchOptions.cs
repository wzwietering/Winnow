namespace EfCoreUtils;

/// <summary>
/// Options for parent-only upsert batch operations.
/// </summary>
public class UpsertBatchOptions
{
    /// <summary>
    /// The batch processing strategy to use. Default is OneByOne for maximum failure isolation.
    /// </summary>
    public BatchStrategy Strategy { get; set; } = BatchStrategy.OneByOne;

    /// <summary>
    /// When true (default), throws if navigation properties are populated.
    /// Use UpsertGraphBatch to upsert parent with children.
    /// </summary>
    public bool ValidateNavigationProperties { get; set; } = true;

    /// <summary>
    /// How to handle duplicate key errors during INSERT attempts.
    /// Default: Fail.
    /// </summary>
    /// <remarks>
    /// <para><strong>Race Condition Mitigation:</strong></para>
    /// <para>
    /// Set to <see cref="DuplicateKeyStrategy.RetryAsUpdate"/> to automatically
    /// retry failed inserts as updates. This handles the case where another process
    /// inserts the same key between key detection and SaveChanges.
    /// </para>
    /// </remarks>
    public DuplicateKeyStrategy DuplicateKeyStrategy { get; set; } = DuplicateKeyStrategy.Fail;
}
