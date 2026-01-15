namespace EfCoreUtils;

/// <summary>
/// Specifies how to handle children when deleting a parent entity in graph operations.
/// </summary>
public enum DeleteCascadeBehavior
{
    /// <summary>
    /// Delete children first, then parent. Safest option -
    /// works regardless of database FK cascade settings.
    /// </summary>
    Cascade,

    /// <summary>
    /// Throw if parent has loaded children.
    /// User must delete children separately first.
    /// </summary>
    Throw,

    /// <summary>
    /// Only delete parent, rely on database CASCADE DELETE.
    /// May fail with FK constraint error if database doesn't cascade.
    /// </summary>
    ParentOnly
}
