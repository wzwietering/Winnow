namespace Winnow.Internal;

/// <summary>
/// Shared context for graph traversal operations providing cycle detection and depth tracking.
/// </summary>
internal sealed class GraphTraversalContext
{
    private readonly HashSet<object> _visited;
    private readonly int _maxDepth;

    internal GraphTraversalContext(int maxDepth)
    {
        _visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        _maxDepth = DepthConstants.ClampDepth(maxDepth);
    }

    /// <summary>
    /// Attempts to visit an entity. Returns false if already visited (cycle detected).
    /// </summary>
    internal bool TryVisit(object entity) => _visited.Add(entity);

    /// <summary>
    /// Returns true if current depth has reached maximum allowed depth.
    /// </summary>
    internal bool IsAtMaxDepth(int currentDepth) => currentDepth >= _maxDepth;

    /// <summary>
    /// Returns true if entity was already visited.
    /// </summary>
    internal bool WasVisited(object entity) => _visited.Contains(entity);

    /// <summary>
    /// Maximum depth for this traversal (clamped to AbsoluteMaxDepth).
    /// </summary>
    internal int MaxDepth => _maxDepth;

    /// <summary>
    /// Creates a new traversal context with standard cycle detection.
    /// </summary>
    internal static GraphTraversalContext Create(int maxDepth) => new(maxDepth);
}
