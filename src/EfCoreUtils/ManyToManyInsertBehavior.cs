namespace EfCoreUtils;

/// <summary>
/// Specifies how related entities in many-to-many navigations are handled during insert.
/// </summary>
public enum ManyToManyInsertBehavior
{
    /// <summary>
    /// Attach related entities as existing (default).
    /// Assumes related entities already exist in database.
    /// Creates join records linking to their IDs without modifying the entities.
    /// </summary>
    AttachExisting,

    /// <summary>
    /// Insert related entities if they have default primary key values, otherwise attach.
    /// Entities with non-default keys are always attached.
    /// </summary>
    InsertIfNew
}
