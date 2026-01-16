namespace EfCoreUtils;

/// <summary>
/// Contains statistics about a graph traversal operation.
/// </summary>
public class GraphTraversalResult<TKey> where TKey : notnull, IEquatable<TKey>
{
    /// <summary>
    /// The maximum depth level reached during traversal.
    /// </summary>
    public int MaxDepthReached { get; init; }

    /// <summary>
    /// Total number of entities traversed across all levels.
    /// </summary>
    public int TotalEntitiesTraversed { get; init; }

    /// <summary>
    /// Count of entities at each depth level.
    /// Key is the depth (0 = root), value is the count.
    /// </summary>
    public IReadOnlyDictionary<int, int> EntitiesByDepth { get; init; } = new Dictionary<int, int>();
}
