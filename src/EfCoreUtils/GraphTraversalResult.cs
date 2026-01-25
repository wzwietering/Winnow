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

    /// <summary>
    /// IDs of processed reference entities, grouped by type name.
    /// Empty if IncludeReferences is false.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<TKey>> ProcessedReferencesByType { get; init; }
        = new Dictionary<string, IReadOnlyList<TKey>>();

    /// <summary>
    /// Count of unique referenced entities processed (deduplicated).
    /// Zero if IncludeReferences is false.
    /// </summary>
    public int UniqueReferencesProcessed { get; init; }

    /// <summary>
    /// Maximum depth reached when traversing references.
    /// Zero if IncludeReferences is false or no references exist.
    /// </summary>
    public int MaxReferenceDepthReached { get; init; }

    /// <summary>
    /// Total many-to-many join records created across all navigations.
    /// Zero if IncludeManyToMany is false.
    /// </summary>
    public int JoinRecordsCreated { get; init; }

    /// <summary>
    /// Total many-to-many join records removed across all navigations.
    /// Zero if IncludeManyToMany is false or operation is insert-only.
    /// </summary>
    public int JoinRecordsRemoved { get; init; }

    /// <summary>
    /// Many-to-many join operations grouped by navigation.
    /// Key format: "TypeName.NavigationName" (e.g., "Student.Courses").
    /// Uses the simple type name (Type.Name), not the fully-qualified namespace.
    /// Value: (Created count, Removed count) for that navigation.
    /// Empty if IncludeManyToMany is false.
    /// </summary>
    public IReadOnlyDictionary<string, (int Created, int Removed)> JoinOperationsByNavigation { get; init; }
        = new Dictionary<string, (int, int)>();
}
