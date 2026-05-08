namespace Winnow;

/// <summary>
/// Base class for graph batch operation options.
/// </summary>
public abstract class GraphOptionsBase
{
    /// <summary>
    /// Strategy to use for batch processing. Default: OneByOne (safer for graphs).
    /// </summary>
    public BatchStrategy Strategy { get; set; } = BatchStrategy.OneByOne;

    /// <summary>
    /// Maximum depth to traverse in the entity graph.
    /// Default: 10. Use to prevent infinite recursion in deep hierarchies.
    /// </summary>
    public int MaxDepth { get; set; } = 10;

    /// <summary>
    /// When true, includes reference navigations (many-to-one) during traversal.
    /// Default: false.
    /// </summary>
    public bool IncludeReferences { get; set; } = false;

    /// <summary>
    /// How to handle circular references during traversal.
    /// Only applies when IncludeReferences is true.
    /// Default: Throw (safest).
    /// </summary>
    public CircularReferenceHandling CircularReferenceHandling { get; set; }
        = CircularReferenceHandling.Throw;

    /// <summary>
    /// When true, includes many-to-many navigations during traversal.
    /// Default: false.
    /// </summary>
    public bool IncludeManyToMany { get; set; } = false;

    /// <summary>
    /// Maximum allowed size for many-to-many collections.
    /// Throws if a collection exceeds this size.
    /// Default: 0 (no limit). Set to a positive value to enable.
    /// </summary>
    public int MaxManyToManyCollectionSize { get; set; } = 0;

    /// <summary>
    /// Optional filter to control which navigation properties are traversed.
    /// Use NavigationFilter.Include() or NavigationFilter.Exclude() to create.
    /// When null (default), all navigations are traversed.
    /// </summary>
    public NavigationFilter? NavigationFilter { get; set; }

    /// <summary>
    /// When set, enables automatic retry with exponential backoff for transient failures.
    /// </summary>
    public RetryOptions? Retry { get; set; }

    /// <summary>
    /// Controls how much per-entity detail the result captures. Default: <see cref="Winnow.ResultDetail.Full"/>.
    /// For graph operations, reducing detail also drops the graph hierarchy tree and traversal statistics,
    /// which are typically the dominant memory cost. <see cref="WinnowResultBase{TKey}.SuccessCount"/>
    /// and <see cref="WinnowResultBase{TKey}.FailureCount"/> remain accurate at every level.
    /// </summary>
    public ResultDetail ResultDetail { get; set; } = ResultDetail.Full;
}
