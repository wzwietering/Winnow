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
}
