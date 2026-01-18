using EfCoreUtils.Internal.Services.MixedKey;
using EfCoreUtils.MixedKey;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EfCoreUtils.Internal.MixedKey;

/// <summary>
/// Builds hierarchical graph representations of entity relationships with mixed key types.
/// </summary>
internal class MixedKeyGraphHierarchyBuilder
{
    private readonly DbContext _context;
    private readonly MixedKeyEntityKeyService _keyService;

    internal MixedKeyGraphHierarchyBuilder(DbContext context)
    {
        _context = context;
        _keyService = new MixedKeyEntityKeyService(context);
    }

    internal (MixedKeyGraphNode Node, MixedKeyGraphTraversalResult Stats) Build(object entity, int maxDepth)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var depthCounts = new Dictionary<int, int>();
        var keyTypeCounts = new Dictionary<Type, int>();

        var rootNode = BuildNodeRecursive(entity, 0, maxDepth, visited, depthCounts, keyTypeCounts);
        var stats = CreateTraversalStats(depthCounts, keyTypeCounts);

        return (rootNode, stats);
    }

    private MixedKeyGraphNode BuildNodeRecursive(
        object entity, int currentDepth, int maxDepth,
        HashSet<object> visited, Dictionary<int, int> depthCounts, Dictionary<Type, int> keyTypeCounts)
    {
        if (!visited.Add(entity))
        {
            return CreateSkippedNode(entity, currentDepth);
        }

        var entry = _context.Entry(entity);
        IncrementDepthCount(depthCounts, currentDepth);
        IncrementKeyTypeCount(keyTypeCounts, entry);

        var children = BuildChildNodes(entry, currentDepth, maxDepth, visited, depthCounts, keyTypeCounts);
        return CreateGraphNode(entry, currentDepth, children);
    }

    private MixedKeyGraphNode CreateSkippedNode(object entity, int depth)
    {
        var entry = _context.Entry(entity);
        var key = _keyService.GetEntityKey(entry);

        return new MixedKeyGraphNode(key.GetValueAsObject(), key.KeyType)
        {
            EntityType = entry.Metadata.ClrType.Name,
            Depth = depth,
            Children = []
        };
    }

    private MixedKeyGraphNode CreateGraphNode(EntityEntry entry, int depth, List<MixedKeyGraphNode> children)
    {
        var key = _keyService.GetEntityKey(entry);

        return new MixedKeyGraphNode(key.GetValueAsObject(), key.KeyType)
        {
            EntityType = entry.Metadata.ClrType.Name,
            Depth = depth,
            Children = children
        };
    }

    private List<MixedKeyGraphNode> BuildChildNodes(
        EntityEntry entry, int currentDepth, int maxDepth,
        HashSet<object> visited, Dictionary<int, int> depthCounts, Dictionary<Type, int> keyTypeCounts)
    {
        if (currentDepth >= maxDepth)
        {
            return [];
        }

        var children = new List<MixedKeyGraphNode>();
        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            AddChildNodesFromNavigation(navigation, currentDepth, maxDepth, visited, depthCounts, keyTypeCounts, children);
        }
        return children;
    }

    private void AddChildNodesFromNavigation(
        NavigationEntry navigation, int currentDepth, int maxDepth,
        HashSet<object> visited, Dictionary<int, int> depthCounts, Dictionary<Type, int> keyTypeCounts,
        List<MixedKeyGraphNode> children)
    {
        foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
        {
            var childNode = BuildNodeRecursive(item, currentDepth + 1, maxDepth, visited, depthCounts, keyTypeCounts);
            children.Add(childNode);
        }
    }

    private static void IncrementDepthCount(Dictionary<int, int> depthCounts, int depth)
    {
        depthCounts.TryGetValue(depth, out var count);
        depthCounts[depth] = count + 1;
    }

    private static void IncrementKeyTypeCount(Dictionary<Type, int> keyTypeCounts, EntityEntry entry)
    {
        var keyProperty = entry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (keyProperty == null)
        {
            return;
        }

        var keyType = keyProperty.ClrType;
        keyTypeCounts.TryGetValue(keyType, out var count);
        keyTypeCounts[keyType] = count + 1;
    }

    private static MixedKeyGraphTraversalResult CreateTraversalStats(
        Dictionary<int, int> depthCounts, Dictionary<Type, int> keyTypeCounts) => new()
    {
        MaxDepthReached = depthCounts.Count > 0 ? depthCounts.Keys.Max() : 0,
        TotalEntitiesTraversed = depthCounts.Values.Sum(),
        EntitiesByDepth = depthCounts,
        EntitiesByKeyType = keyTypeCounts
    };
}
