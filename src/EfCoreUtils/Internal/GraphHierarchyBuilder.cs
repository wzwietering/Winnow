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
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var depthCounts = new Dictionary<int, int>();

        var rootNode = BuildNodeRecursive(entity, 0, maxDepth, visited, depthCounts);
        var stats = CreateTraversalStats(depthCounts);

        return (rootNode, stats);
    }

    private GraphNode<TKey> BuildNodeRecursive(
        object entity, int currentDepth, int maxDepth,
        HashSet<object> visited, Dictionary<int, int> depthCounts)
    {
        if (!visited.Add(entity))
        {
            return CreateSkippedNode(entity, currentDepth);
        }

        IncrementDepthCount(depthCounts, currentDepth);
        var entry = _context.Entry(entity);
        var children = BuildChildNodes(entry, currentDepth, maxDepth, visited, depthCounts);

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
        EntityEntry entry, int currentDepth, int maxDepth,
        HashSet<object> visited, Dictionary<int, int> depthCounts)
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

            AddChildNodesFromNavigation(navigation, currentDepth, maxDepth, visited, depthCounts, children);
        }
        return children;
    }

    private void AddChildNodesFromNavigation(
        NavigationEntry navigation, int currentDepth, int maxDepth,
        HashSet<object> visited, Dictionary<int, int> depthCounts,
        List<GraphNode<TKey>> children)
    {
        foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
        {
            var childNode = BuildNodeRecursive(item, currentDepth + 1, maxDepth, visited, depthCounts);
            children.Add(childNode);
        }
    }

    private static void IncrementDepthCount(Dictionary<int, int> depthCounts, int depth)
    {
        depthCounts.TryGetValue(depth, out var count);
        depthCounts[depth] = count + 1;
    }

    private static GraphTraversalResult<TKey> CreateTraversalStats(Dictionary<int, int> depthCounts) => new()
    {
        MaxDepthReached = depthCounts.Count > 0 ? depthCounts.Keys.Max() : 0,
        TotalEntitiesTraversed = depthCounts.Values.Sum(),
        EntitiesByDepth = depthCounts
    };

    // ========== Reference-Aware Build Methods ==========

    internal (GraphNode<TKey> Node, GraphTraversalResult<TKey> Stats) BuildWithReferences(
        object entity, int maxDepth)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var depthCounts = new Dictionary<int, int>();
        var referencesByType = new Dictionary<string, List<TKey>>();
        var maxRefDepth = 0;

        var rootNode = BuildNodeWithReferencesRecursive(
            entity, 0, maxDepth, visited, depthCounts, referencesByType, ref maxRefDepth);
        var stats = CreateTraversalStatsWithReferences(depthCounts, referencesByType, maxRefDepth);

        return (rootNode, stats);
    }

    private GraphNode<TKey> BuildNodeWithReferencesRecursive(
        object entity, int currentDepth, int maxDepth,
        HashSet<object> visited, Dictionary<int, int> depthCounts,
        Dictionary<string, List<TKey>> referencesByType, ref int maxRefDepth)
    {
        if (!visited.Add(entity))
        {
            return CreateSkippedNode(entity, currentDepth);
        }

        IncrementDepthCount(depthCounts, currentDepth);
        var entry = _context.Entry(entity);
        var children = BuildChildNodesWithReferences(
            entry, currentDepth, maxDepth, visited, depthCounts, referencesByType, ref maxRefDepth);

        TrackReferences(entry, currentDepth, maxDepth, visited, referencesByType, ref maxRefDepth);

        return CreateGraphNode(entry, currentDepth, children);
    }

    private List<GraphNode<TKey>> BuildChildNodesWithReferences(
        EntityEntry entry, int currentDepth, int maxDepth,
        HashSet<object> visited, Dictionary<int, int> depthCounts,
        Dictionary<string, List<TKey>> referencesByType, ref int maxRefDepth)
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

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                var childNode = BuildNodeWithReferencesRecursive(
                    item, currentDepth + 1, maxDepth, visited, depthCounts, referencesByType, ref maxRefDepth);
                children.Add(childNode);
            }
        }
        return children;
    }

    private void TrackReferences(
        EntityEntry entry, int currentDepth, int maxDepth,
        HashSet<object> visited, Dictionary<string, List<TKey>> referencesByType, ref int maxRefDepth)
    {
        if (currentDepth >= maxDepth)
        {
            return;
        }

        foreach (var navigation in NavigationPropertyHelper.GetReferenceNavigations(entry))
        {
            var refEntity = NavigationPropertyHelper.GetReferenceValue(navigation);
            if (refEntity == null || !visited.Add(refEntity))
            {
                continue;
            }

            var refEntry = _context.Entry(refEntity);
            var typeName = refEntry.Metadata.ClrType.Name;
            var entityId = _getEntityId(refEntry);

            AddReferenceToTracking(referencesByType, typeName, entityId);
            maxRefDepth = Math.Max(maxRefDepth, currentDepth + 1);

            TrackReferences(refEntry, currentDepth + 1, maxDepth, visited, referencesByType, ref maxRefDepth);
        }
    }

    private static void AddReferenceToTracking(
        Dictionary<string, List<TKey>> referencesByType, string typeName, TKey entityId)
    {
        if (!referencesByType.TryGetValue(typeName, out var list))
        {
            list = [];
            referencesByType[typeName] = list;
        }
        list.Add(entityId);
    }

    private static GraphTraversalResult<TKey> CreateTraversalStatsWithReferences(
        Dictionary<int, int> depthCounts,
        Dictionary<string, List<TKey>> referencesByType,
        int maxRefDepth)
    {
        var processedRefs = referencesByType.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<TKey>)kvp.Value.AsReadOnly());

        return new GraphTraversalResult<TKey>
        {
            MaxDepthReached = depthCounts.Count > 0 ? depthCounts.Keys.Max() : 0,
            TotalEntitiesTraversed = depthCounts.Values.Sum(),
            EntitiesByDepth = depthCounts,
            ProcessedReferencesByType = processedRefs,
            UniqueReferencesProcessed = referencesByType.Values.Sum(list => list.Count),
            MaxReferenceDepthReached = maxRefDepth
        };
    }
}
