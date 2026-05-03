using Winnow.Internal;

namespace Winnow;

/// <summary>
/// Base class for batch operation results, providing common computed properties.
/// </summary>
public abstract class WinnowResultBase<TKey> where TKey : notnull, IEquatable<TKey>
{
    private readonly int? _successCount;
    private readonly int? _failureCount;
    private readonly IReadOnlyList<GraphNode<TKey>>? _graphHierarchy;
    private readonly GraphTraversalResult<TKey>? _traversalInfo;

    /// <summary>
    /// The detail level at which this result was captured. Properties whose data
    /// was not collected throw <see cref="InvalidOperationException"/> on access.
    /// </summary>
    public ResultDetail ResultDetail { get; init; } = ResultDetail.Full;

    /// <summary>
    /// Number of successfully processed entities. Always available.
    /// </summary>
    public int SuccessCount
    {
        get => _successCount ?? GetCollectionSuccessCount();
        init => _successCount = value;
    }

    /// <summary>
    /// Number of failed entities. Always available.
    /// </summary>
    public int FailureCount
    {
        get => _failureCount ?? GetCollectionFailureCount();
        init => _failureCount = value;
    }

    /// <summary>
    /// Computes the success count from the underlying collection when no
    /// explicit value was supplied. Derived types override to report the
    /// size of their captured success collection.
    /// </summary>
    protected virtual int GetCollectionSuccessCount() => 0;

    /// <summary>
    /// Computes the failure count from the underlying collection when no
    /// explicit value was supplied.
    /// </summary>
    protected virtual int GetCollectionFailureCount() => 0;

    /// <summary>
    /// Total number of entities processed (success + failure).
    /// </summary>
    public int TotalProcessed => SuccessCount + FailureCount;

    /// <summary>
    /// Ratio of successful entities to total processed (0-1).
    /// </summary>
    public double SuccessRate => TotalProcessed > 0 ? (double)SuccessCount / TotalProcessed : 0;

    /// <summary>
    /// Time taken for the batch operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Number of database round trips performed.
    /// </summary>
    public int DatabaseRoundTrips { get; init; }

    /// <summary>
    /// For graph operations only: full hierarchy of processed entities.
    /// Null for parent-only operations. Throws when <see cref="ResultDetail"/>
    /// is lower than <see cref="ResultDetail.Full"/>.
    /// </summary>
    public IReadOnlyList<GraphNode<TKey>>? GraphHierarchy
    {
        get => ResultDetail >= ResultDetail.Full
            ? _graphHierarchy
            : throw ResultDetailGuard.NotCaptured(nameof(GraphHierarchy), ResultDetail.Full, ResultDetail);
        init => _graphHierarchy = value;
    }

    internal IReadOnlyList<GraphNode<TKey>>? GraphHierarchyRaw => _graphHierarchy;

    /// <summary>
    /// For graph operations only: statistics about the traversal.
    /// Null for parent-only operations. Throws when <see cref="ResultDetail"/>
    /// is lower than <see cref="ResultDetail.Full"/>.
    /// </summary>
    public GraphTraversalResult<TKey>? TraversalInfo
    {
        get => ResultDetail >= ResultDetail.Full
            ? _traversalInfo
            : throw ResultDetailGuard.NotCaptured(nameof(TraversalInfo), ResultDetail.Full, ResultDetail);
        init => _traversalInfo = value;
    }

    internal GraphTraversalResult<TKey>? TraversalInfoRaw => _traversalInfo;

    /// <summary>
    /// True if all entities succeeded and the operation was not cancelled.
    /// </summary>
    public bool IsCompleteSuccess => FailureCount == 0 && SuccessCount > 0 && !WasCancelled;

    /// <summary>
    /// True if all entities failed.
    /// </summary>
    public bool IsCompleteFailure => SuccessCount == 0 && FailureCount > 0;

    /// <summary>
    /// True if some entities succeeded and some failed.
    /// </summary>
    public bool IsPartialSuccess => SuccessCount > 0 && FailureCount > 0;

    /// <summary>
    /// Indicates whether the operation was cancelled before completing.
    /// When true, some entities may not have been processed.
    /// </summary>
    public bool WasCancelled { get; init; }

    /// <summary>
    /// Total number of transient failure retries across the operation.
    /// Zero when RetryOptions is not configured.
    /// </summary>
    public int TotalRetries { get; init; }
}
