namespace Winnow.Internal;

/// <summary>
/// Result of attempting to refresh an entity's primary key from a MatchBy lookup
/// during the duplicate-key retry path.
/// </summary>
internal enum MatchByRefreshOutcome
{
    /// <summary>
    /// MatchBy is not configured for this operation, or the entity has null match values.
    /// The retry path should fall through to legacy behavior.
    /// </summary>
    NotApplicable,

    /// <summary>
    /// MatchBy is configured and was queried, but no existing row matched the entity's
    /// match values. The retry path should record a failure and skip the no-op UPDATE.
    /// </summary>
    NotFound,

    /// <summary>
    /// MatchBy refresh succeeded. The entity's primary key and concurrency tokens
    /// have been copied from the matched row; the retry path should proceed to UPDATE.
    /// </summary>
    Refreshed
}
