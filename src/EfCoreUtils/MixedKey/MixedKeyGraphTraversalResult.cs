namespace EfCoreUtils.MixedKey;

/// <summary>
/// Contains statistics about a graph traversal operation with mixed key types.
/// </summary>
public class MixedKeyGraphTraversalResult
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

    /// <summary>
    /// Count of entities by their key type.
    /// Key is the CLR type of the key, value is the count.
    /// </summary>
    public IReadOnlyDictionary<Type, int> EntitiesByKeyType { get; init; } = new Dictionary<Type, int>();
}
