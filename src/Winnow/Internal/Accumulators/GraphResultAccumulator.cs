using Winnow.Internal.Services;

namespace Winnow.Internal.Accumulators;

/// <summary>
/// Accumulates graph hierarchy nodes and traversal statistics for graph operations.
/// All capture is suppressed when <see cref="ResultDetail"/> is below
/// <see cref="ResultDetail.Full"/>; the operation continues to call into the
/// accumulator so all reporting paths route through one place. Correctness-side
/// trackers (link change tracking, orphan tracking) live elsewhere and are
/// always active.
/// </summary>
internal sealed class GraphResultAccumulator<TKey> where TKey : notnull, IEquatable<TKey>
{
    private readonly ResultDetail _detail;
    private readonly List<GraphNode<TKey>>? _hierarchy;
    private readonly GraphStatisticsTracker<TKey>? _stats;

    internal GraphResultAccumulator(ResultDetail detail)
    {
        _detail = detail;
        if (detail < ResultDetail.Full)
        {
            return;
        }
        _hierarchy = [];
        _stats = new GraphStatisticsTracker<TKey>();
    }

    internal bool IsActive => _detail >= ResultDetail.Full;

    internal void AddHierarchyNode(GraphNode<TKey> node, GraphTraversalResult<TKey> stats)
    {
        if (!IsActive)
        {
            return;
        }
        _hierarchy!.Add(node);
        _stats!.AggregateStats(stats);
    }

    internal void AggregateReferenceStats(ReferenceTrackingResult refResult)
    {
        if (!IsActive)
        {
            return;
        }
        _stats!.AggregateReferenceStats(refResult);
    }

    internal void AggregateManyToManyStats(ManyToManyStatisticsTracker tracker)
    {
        if (!IsActive)
        {
            return;
        }
        _stats!.AggregateManyToManyStats(tracker);
    }

    internal IReadOnlyList<GraphNode<TKey>>? Hierarchy => _hierarchy;

    internal GraphTraversalResult<TKey>? TraversalInfo => _stats?.CreateTraversalInfo();
}
