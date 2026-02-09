namespace EfCoreUtils;

/// <summary>
/// Immutable, thread-safe filter that controls which navigation properties are traversed
/// during graph operations. Use <see cref="Include"/> or <see cref="Exclude"/> to create.
/// </summary>
public sealed class NavigationFilter
{
    private readonly IReadOnlyDictionary<Type, IReadOnlySet<string>> _rules;

    /// <summary>
    /// True for include (allowlist) mode, false for exclude (blocklist) mode.
    /// </summary>
    public bool IsIncludeMode { get; }

    internal NavigationFilter(
        IReadOnlyDictionary<Type, IReadOnlySet<string>> rules, bool isIncludeMode)
    {
        _rules = rules;
        IsIncludeMode = isIncludeMode;
    }

    /// <summary>
    /// Creates an include (allowlist) filter builder. Only explicitly listed navigations will be traversed.
    /// Entity types without rules will have NO navigations traversed.
    /// </summary>
    public static NavigationFilterBuilder Include() => new(isIncludeMode: true);

    /// <summary>
    /// Creates an exclude (blocklist) filter builder. Listed navigations will be skipped,
    /// all others will be traversed normally.
    /// </summary>
    public static NavigationFilterBuilder Exclude() => new(isIncludeMode: false);

    /// <summary>
    /// Determines whether a navigation property should be traversed.
    /// </summary>
    internal bool ShouldTraverse(Type entityType, string navigationName)
    {
        if (IsIncludeMode)
        {
            return IsNavigationListed(entityType, navigationName);
        }

        return !IsNavigationListed(entityType, navigationName);
    }

    private bool IsNavigationListed(Type entityType, string navigationName) =>
        _rules.TryGetValue(entityType, out var set) && set.Contains(navigationName);

    internal IReadOnlyDictionary<Type, IReadOnlySet<string>> Rules => _rules;

    public override string ToString()
    {
        var mode = IsIncludeMode ? "Include" : "Exclude";
        var rulesList = string.Join(", ", _rules.Select(kvp =>
            $"{kvp.Key.Name}: [{string.Join(", ", kvp.Value)}]"));
        return $"NavigationFilter ({mode}): {rulesList}";
    }
}
