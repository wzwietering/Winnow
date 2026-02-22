namespace Winnow;

/// <summary>
/// Strategy for processing batch operations.
/// </summary>
public enum BatchStrategy
{
    /// <summary>
    /// Processes entities one at a time. Safer but slower.
    /// </summary>
    OneByOne,

    /// <summary>
    /// Attempts bulk save first, then recursively splits on failure to isolate bad entities.
    /// </summary>
    DivideAndConquer
}
