using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EfCoreUtils.Internal;

/// <summary>
/// Builds hierarchical graph representations of entity relationships.
/// </summary>
internal class GraphHierarchyBuilder<TKey>
    where TKey : notnull, IEquatable<TKey>
{
    private readonly DbContext _context;
    private readonly Func<EntityEntry, TKey> _getEntityId;

    internal GraphHierarchyBuilder(DbContext context, Func<EntityEntry, TKey> getEntityId)
    {
        _context = context;
        _getEntityId = getEntityId;
    }

    internal (GraphNode<TKey> Node, GraphTraversalResult<TKey> Stats) Build(object entity, int maxDepth)
    {
        var context = new TraversalContext { IncludeReferences = false };
        return BuildInternal(entity, maxDepth, context);
    }

    internal (GraphNode<TKey> Node, GraphTraversalResult<TKey> Stats) BuildWithReferences(object entity, int maxDepth)
    {
        var context = new TraversalContext { IncludeReferences = true };
        return BuildInternal(entity, maxDepth, context);
    }

    private (GraphNode<TKey> Node, GraphTraversalResult<TKey> Stats) BuildInternal(
        object entity, int maxDepth, TraversalContext context)
    {
        var rootNode = BuildNodeRecursive(entity, 0, maxDepth, context);
        var stats = CreateTraversalStats(context);
        return (rootNode, stats);
    }

    private GraphNode<TKey> BuildNodeRecursive(
        object entity, int currentDepth, int maxDepth, TraversalContext context)
    {
        if (!context.Visited.Add(entity))
        {
            return CreateSkippedNode(entity, currentDepth);
        }

        context.IncrementDepthCount(currentDepth);
        var entry = _context.Entry(entity);
        var children = BuildChildNodes(entry, currentDepth, maxDepth, context);

        if (context.IncludeReferences)
        {
            TrackReferences(entry, currentDepth, maxDepth, context);
        }

        return CreateGraphNode(entry, currentDepth, children);
    }

    private GraphNode<TKey> CreateSkippedNode(object entity, int depth)
    {
        var entry = _context.Entry(entity);
        return new GraphNode<TKey>
        {
            EntityId = _getEntityId(entry),
            EntityType = entry.Metadata.ClrType.Name,
            Depth = depth,
            Children = []
        };
    }

    private GraphNode<TKey> CreateGraphNode(EntityEntry entry, int depth, List<GraphNode<TKey>> children) => new()
    {
        EntityId = _getEntityId(entry),
        EntityType = entry.Metadata.ClrType.Name,
        Depth = depth,
        Children = children
    };

    private List<GraphNode<TKey>> BuildChildNodes(
        EntityEntry entry, int currentDepth, int maxDepth, TraversalContext context)
    {
        if (currentDepth >= maxDepth)
        {
            return [];
        }

        var children = new List<GraphNode<TKey>>();
        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            AddChildNodesFromNavigation(navigation, currentDepth, maxDepth, context, children);
        }
        return children;
    }

    private void AddChildNodesFromNavigation(
        NavigationEntry navigation, int currentDepth, int maxDepth,
        TraversalContext context, List<GraphNode<TKey>> children)
    {
        foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
        {
            var childNode = BuildNodeRecursive(item, currentDepth + 1, maxDepth, context);
            children.Add(childNode);
        }
    }

    // ========== Reference Tracking Methods ==========

    private void TrackReferences(EntityEntry entry, int currentDepth, int maxDepth, TraversalContext context)
    {
        if (currentDepth >= maxDepth)
        {
            return;
        }

        foreach (var navigation in NavigationPropertyHelper.GetReferenceNavigations(entry))
        {
            TrackSingleReference(navigation, currentDepth, maxDepth, context);
        }
    }

    private void TrackSingleReference(
        NavigationEntry navigation, int currentDepth, int maxDepth, TraversalContext context)
    {
        var refEntity = NavigationPropertyHelper.GetReferenceValue(navigation);
        if (refEntity == null || !context.Visited.Add(refEntity))
        {
            return;
        }

        var refEntry = _context.Entry(refEntity);
        context.AddReference(refEntry.Metadata.ClrType.Name, _getEntityId(refEntry), currentDepth + 1);

        TrackReferences(refEntry, currentDepth + 1, maxDepth, context);
    }

    // ========== Stats Creation ==========

    private GraphTraversalResult<TKey> CreateTraversalStats(TraversalContext context)
    {
        if (!context.IncludeReferences)
        {
            return CreateBasicStats(context);
        }
        return CreateStatsWithReferences(context);
    }

    private static GraphTraversalResult<TKey> CreateBasicStats(TraversalContext context) => new()
    {
        MaxDepthReached = context.DepthCounts.Count > 0 ? context.DepthCounts.Keys.Max() : 0,
        TotalEntitiesTraversed = context.DepthCounts.Values.Sum(),
        EntitiesByDepth = context.DepthCounts
    };

    private static GraphTraversalResult<TKey> CreateStatsWithReferences(TraversalContext context)
    {
        var processedRefs = context.ReferencesByType.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<TKey>)kvp.Value.AsReadOnly());

        return new GraphTraversalResult<TKey>
        {
            MaxDepthReached = context.DepthCounts.Count > 0 ? context.DepthCounts.Keys.Max() : 0,
            TotalEntitiesTraversed = context.DepthCounts.Values.Sum(),
            EntitiesByDepth = context.DepthCounts,
            ProcessedReferencesByType = processedRefs,
            UniqueReferencesProcessed = context.ReferencesByType.Values.Sum(list => list.Count),
            MaxReferenceDepthReached = context.MaxRefDepth
        };
    }

    // ========== Traversal Context ==========

    private sealed class TraversalContext
    {
        public HashSet<object> Visited { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<int, int> DepthCounts { get; } = new();
        public Dictionary<string, List<TKey>> ReferencesByType { get; } = new();
        public int MaxRefDepth { get; set; }
        public bool IncludeReferences { get; init; }

        public void IncrementDepthCount(int depth)
        {
            DepthCounts.TryGetValue(depth, out var count);
            DepthCounts[depth] = count + 1;
        }

        public void AddReference(string typeName, TKey entityId, int depth)
        {
            if (!ReferencesByType.TryGetValue(typeName, out var list))
            {
                list = [];
                ReferencesByType[typeName] = list;
            }
            list.Add(entityId);
            MaxRefDepth = Math.Max(MaxRefDepth, depth);
        }
    }
}
