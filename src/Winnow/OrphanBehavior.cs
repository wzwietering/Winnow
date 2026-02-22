namespace Winnow;

/// <summary>
/// Specifies how to handle children removed from collections during graph updates.
/// </summary>
public enum OrphanBehavior
{
    /// <summary>
    /// Throw exception if any children are removed from collections.
    /// This is the safest option - prevents accidental data loss.
    /// </summary>
    Throw,

    /// <summary>
    /// Delete orphaned children from the database.
    /// Matches EF Core cascade delete behavior.
    /// </summary>
    Delete,

    /// <summary>
    /// Leave orphaned children in database (detach from parent).
    /// Warning: May violate FK constraints if child has required FK.
    /// </summary>
    Detach
}
