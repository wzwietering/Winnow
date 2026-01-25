using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EfCoreUtils.Internal;

/// <summary>
/// Static utility methods for detecting and working with many-to-many navigations.
/// </summary>
internal static class ManyToManyNavigationHelper
{
    /// <summary>
    /// Returns true if the navigation is a skip navigation (many-to-many without explicit join entity).
    /// </summary>
    internal static bool IsSkipNavigation(NavigationEntry navigation) =>
        navigation.Metadata is ISkipNavigation;

    /// <summary>
    /// Returns true if the navigation represents a many-to-many relationship.
    /// Detects both skip navigations and explicit join entity patterns.
    /// </summary>
    internal static bool IsManyToManyNavigation(NavigationEntry navigation)
    {
        if (navigation.Metadata is ISkipNavigation)
        {
            return true;
        }

        return IsExplicitJoinEntityNavigation(navigation);
    }

    /// <summary>
    /// For skip navigations, returns the target entity type (the "other side").
    /// For explicit joins, returns the join entity type.
    /// Returns null for non-many-to-many navigations.
    /// </summary>
    internal static Type? GetManyToManyTargetType(NavigationEntry navigation) =>
        IsManyToManyNavigation(navigation) ? navigation.Metadata.TargetEntityType.ClrType : null;

    /// <summary>
    /// For skip navigations, returns the join entity type that EF Core manages internally.
    /// Returns null for explicit join entities or non-many-to-many navigations.
    /// </summary>
    internal static Type? GetSkipNavigationJoinType(NavigationEntry navigation) =>
        navigation.Metadata is ISkipNavigation skipNav ? skipNav.JoinEntityType.ClrType : null;

    /// <summary>
    /// Gets all many-to-many navigations for an entity entry that have values.
    /// </summary>
    internal static IEnumerable<NavigationEntry> GetManyToManyNavigations(EntityEntry entry) =>
        entry.Navigations.Where(n => n.CurrentValue != null && IsManyToManyNavigation(n));

    /// <summary>
    /// Checks if navigation points to an explicit join entity.
    /// An explicit join entity has exactly 2 foreign keys to different principal types.
    /// </summary>
    private static bool IsExplicitJoinEntityNavigation(NavigationEntry navigation)
    {
        if (!navigation.Metadata.IsCollection)
        {
            return false;
        }

        var targetType = navigation.Metadata.TargetEntityType;
        var foreignKeys = targetType.GetForeignKeys().ToList();

        if (foreignKeys.Count != 2)
        {
            return false;
        }

        var principalTypes = foreignKeys
            .Select(fk => fk.PrincipalEntityType)
            .Distinct()
            .ToList();

        return principalTypes.Count == 2;
    }
}
