namespace EfCoreUtils.MixedKey;

/// <summary>
/// Represents a node in an entity graph hierarchy with mixed key types.
/// Provides type-safe access to entity IDs regardless of key type.
/// </summary>
public class MixedKeyGraphNode
{
    private readonly object _entityId;

    /// <summary>
    /// The CLR type of the entity's key.
    /// </summary>
    public Type KeyType { get; }

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
    public IReadOnlyList<MixedKeyGraphNode> Children { get; init; } = [];

    /// <summary>
    /// Creates a new mixed key graph node.
    /// </summary>
    internal MixedKeyGraphNode(object entityId, Type keyType)
    {
        _entityId = entityId ?? throw new ArgumentNullException(nameof(entityId));
        KeyType = keyType ?? throw new ArgumentNullException(nameof(keyType));
    }

    /// <summary>
    /// Gets the entity ID as the specified type.
    /// Throws if the type doesn't match.
    /// </summary>
    public TKey GetId<TKey>() where TKey : notnull
    {
        if (typeof(TKey) != KeyType)
        {
            throw new InvalidOperationException(
                $"Key type mismatch for {EntityType}. Requested {typeof(TKey).Name}, actual type is {KeyType.Name}.");
        }

        return (TKey)_entityId;
    }

    /// <summary>
    /// Attempts to get the entity ID as the specified type.
    /// Returns false if the type doesn't match.
    /// </summary>
    public bool TryGetId<TKey>(out TKey? id) where TKey : notnull
    {
        if (typeof(TKey) == KeyType)
        {
            id = (TKey)_entityId;
            return true;
        }

        id = default;
        return false;
    }

    /// <summary>
    /// Gets the entity ID as an object.
    /// </summary>
    public object GetIdAsObject() => _entityId;

    /// <summary>
    /// Returns all descendant nodes by flattening the tree recursively.
    /// </summary>
    public IReadOnlyList<MixedKeyGraphNode> GetAllDescendants()
    {
        var result = new List<MixedKeyGraphNode>();
        CollectDescendants(this, result);
        return result;
    }

    /// <summary>
    /// Returns IDs of descendants that match the specified key type.
    /// </summary>
    public IReadOnlyList<TKey> GetDescendantIdsOfType<TKey>() where TKey : notnull
    {
        var result = new List<TKey>();
        CollectDescendantIdsOfType(this, result);
        return result;
    }

    private static void CollectDescendants(MixedKeyGraphNode node, List<MixedKeyGraphNode> result)
    {
        foreach (var child in node.Children)
        {
            result.Add(child);
            CollectDescendants(child, result);
        }
    }

    private static void CollectDescendantIdsOfType<TKey>(MixedKeyGraphNode node, List<TKey> result) where TKey : notnull
    {
        foreach (var child in node.Children)
        {
            if (child.TryGetId<TKey>(out var id))
            {
                result.Add(id!);
            }

            CollectDescendantIdsOfType(child, result);
        }
    }
}
