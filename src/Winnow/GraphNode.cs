namespace Winnow;

/// <summary>
/// Represents a node in the entity graph hierarchy.
/// Provides recursive structure for arbitrary-depth entity relationships.
/// </summary>
public class GraphNode<TKey> where TKey : notnull, IEquatable<TKey>
{
    /// <summary>
    /// The ID of the entity this node represents.
    /// </summary>
    public required TKey EntityId { get; init; }

    /// <summary>
    /// The CLR type name of the entity.
    /// </summary>
    public string EntityType { get; init; } = string.Empty;

    /// <summary>
    /// The depth level in the hierarchy (0 = root).
    /// </summary>
    public int Depth { get; init; }

    /// <summary>
    /// Child nodes in the hierarchy.
    /// </summary>
    public IReadOnlyList<GraphNode<TKey>> Children { get; init; } = [];

    /// <summary>
    /// Returns all descendant IDs by flattening the tree recursively.
    /// Includes cycle protection to handle graphs with back-references.
    /// </summary>
    public IReadOnlyList<TKey> GetAllDescendantIds()
    {
        var result = new List<TKey>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        visited.Add(this);
        CollectDescendantIds(this, result, visited);
        return result;
    }

    /// <summary>
    /// Returns only immediate child IDs.
    /// </summary>
    public IReadOnlyList<TKey> GetChildIds() => Children.Select(c => c.EntityId).ToList();

    private static void CollectDescendantIds(GraphNode<TKey> node, List<TKey> result, HashSet<object> visited)
    {
        foreach (var child in node.Children)
        {
            if (!visited.Add(child)) continue;

            result.Add(child.EntityId);
            CollectDescendantIds(child, result, visited);
        }
    }
}
