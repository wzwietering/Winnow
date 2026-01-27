namespace EfCoreUtils;

/// <summary>
/// Specifies how circular references are handled during graph traversal.
/// </summary>
public enum CircularReferenceHandling
{
    /// <summary>
    /// Throw exception when any circular reference is detected.
    /// This includes direct self-references (entity.Parent = entity).
    /// Safest option - prevents infinite loops. (Default)
    /// </summary>
    Throw,

    /// <summary>
    /// Process each unique entity once, skip subsequent circular references.
    /// Direct self-references (entity.Parent = entity) still throw
    /// as they typically indicate a programming error.
    /// Use when you have legitimate bidirectional navigations.
    /// </summary>
    Ignore,

    /// <summary>
    /// Process each unique entity once, including direct self-references.
    /// Use only if your domain legitimately requires an entity to reference itself.
    /// Most applications should use Ignore instead.
    /// </summary>
    IgnoreAll
}
