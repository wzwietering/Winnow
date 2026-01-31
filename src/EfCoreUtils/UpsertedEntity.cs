namespace EfCoreUtils;

/// <summary>
/// The type of operation performed during upsert.
/// </summary>
public enum UpsertOperation
{
    /// <summary>Entity was inserted (had default key value).</summary>
    Insert,

    /// <summary>Entity was updated (had non-default key value).</summary>
    Update
}

/// <summary>
/// Represents a successfully upserted entity.
/// </summary>
public class UpsertedEntity<TKey> where TKey : notnull, IEquatable<TKey>
{
    /// <summary>
    /// The entity's primary key (generated for inserts, existing for updates).
    /// </summary>
    public TKey Id { get; init; } = default!;

    /// <summary>
    /// Position in the original input collection.
    /// </summary>
    public int OriginalIndex { get; init; }

    /// <summary>
    /// Reference to the entity.
    /// </summary>
    public object Entity { get; init; } = null!;

    /// <summary>
    /// The operation that was performed on this entity.
    /// </summary>
    public UpsertOperation Operation { get; init; }
}
