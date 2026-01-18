namespace EfCoreUtils;

/// <summary>
/// Specifies how circular references are handled during graph traversal.
/// </summary>
public enum CircularReferenceHandling
{
    /// <summary>
    /// Throw exception when circular reference detected.
    /// Safest option - prevents infinite loops. (Default)
    /// </summary>
    Throw,

    /// <summary>
    /// Process each unique entity once, skip subsequent circular references.
    /// Use when you have legitimate bidirectional navigations.
    /// </summary>
    Ignore
}
