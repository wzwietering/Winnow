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
    /// Insert related entities if they appear to be new, otherwise attach as existing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An entity is considered "new" and will be inserted if it meets either condition:
    /// <list type="bullet">
    ///   <item><description>It has a temporary key (EF Core generated during Add())</description></item>
    ///   <item><description>Its primary key has a default value (0 for int, Guid.Empty for Guid, etc.)</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Entities with non-default, non-temporary keys are attached as
    /// <see cref="Microsoft.EntityFrameworkCore.EntityState.Unchanged"/> and will not be
    /// modified or re-inserted. This allows mixing new and existing entities in the
    /// same many-to-many collection.
    /// </para>
    /// <para>
    /// <b>Example:</b> A Student with new Course objects (Id=0) and existing Course
    /// references (Id=5) - the new courses are inserted, existing ones are linked.
    /// </para>
    /// </remarks>
    InsertIfNew
}
