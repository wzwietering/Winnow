using Winnow.Internal.Services;

namespace Winnow.Internal;

internal class GraphStatisticsTracker<TKey>
    where TKey : notnull, IEquatable<TKey>
{
    private int _totalEntitiesTraversed;
    private int _maxDepthReached;
    private readonly Dictionary<int, int> _entitiesByDepth = [];
    private readonly Dictionary<string, List<TKey>> _processedReferencesByType = [];
    private int _maxReferenceDepthReached;
    private int _joinRecordsCreated;
    private int _joinRecordsRemoved;
    private readonly Dictionary<string, (int Created, int Removed)> _joinOperationsByNavigation = [];

    internal void AggregateStats(GraphTraversalResult<TKey> stats)
    {
        _totalEntitiesTraversed += stats.TotalEntitiesTraversed;
        _maxDepthReached = Math.Max(_maxDepthReached, stats.MaxDepthReached);

        foreach (var (depth, count) in stats.EntitiesByDepth)
        {
            _entitiesByDepth.TryGetValue(depth, out var existing);
            _entitiesByDepth[depth] = existing + count;
        }
    }

    internal void AggregateReferenceStats(ReferenceTrackingResult refResult)
    {
        foreach (var (typeName, ids) in refResult.ProcessedReferencesByType)
        {
            if (!_processedReferencesByType.TryGetValue(typeName, out var list))
            {
                list = [];
                _processedReferencesByType[typeName] = list;
            }

            foreach (var id in ids)
            {
                if (id is TKey typedId)
                {
                    list.Add(typedId);
                }
            }
        }
        _maxReferenceDepthReached = Math.Max(_maxReferenceDepthReached, refResult.MaxReferenceDepthReached);
    }

    internal void AggregateManyToManyStats(ManyToManyStatisticsTracker tracker)
    {
        var (created, removed, byNav) = tracker.GetStatistics();
        _joinRecordsCreated += created;
        _joinRecordsRemoved += removed;

        foreach (var (navKey, stats) in byNav)
        {
            _joinOperationsByNavigation.TryGetValue(navKey, out var existing);
            _joinOperationsByNavigation[navKey] = (existing.Created + stats.Created, existing.Removed + stats.Removed);
        }
    }

    internal GraphTraversalResult<TKey> CreateTraversalInfo()
    {
        var processedRefs = _processedReferencesByType.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<TKey>)kvp.Value.AsReadOnly());

        var joinOps = _joinOperationsByNavigation.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value);

        return new GraphTraversalResult<TKey>
        {
            MaxDepthReached = _maxDepthReached,
            TotalEntitiesTraversed = _totalEntitiesTraversed,
            EntitiesByDepth = _entitiesByDepth,
            ProcessedReferencesByType = processedRefs,
            UniqueReferencesProcessed = _processedReferencesByType.Values.Sum(list => list.Count),
            MaxReferenceDepthReached = _maxReferenceDepthReached,
            JoinRecordsCreated = _joinRecordsCreated,
            JoinRecordsRemoved = _joinRecordsRemoved,
            JoinOperationsByNavigation = joinOps
        };
    }

    internal GraphTraversalResult<TKey> CreateBasicTraversalInfo() => new()
    {
        MaxDepthReached = _maxDepthReached,
        TotalEntitiesTraversed = _totalEntitiesTraversed,
        EntitiesByDepth = _entitiesByDepth
    };
}
