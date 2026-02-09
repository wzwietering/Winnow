using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EfCoreUtils.Internal;

/// <summary>
/// Static helper methods for common graph traversal patterns.
/// </summary>
internal static class TraversalHelper
{
    /// <summary>
    /// Traverses collection children of an entity entry, invoking action for each.
    /// </summary>
    internal static void TraverseCollectionChildren(
        EntityEntry entry,
        Action<object> childAction,
        bool skipManyToMany = true,
        NavigationFilter? filter = null)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!ShouldTraverseCollection(navigation, filter, skipManyToMany))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                childAction(item);
            }
        }
    }

    /// <summary>
    /// Traverses reference navigations of an entity entry, invoking action for each.
    /// </summary>
    internal static void TraverseReferenceNavigations(
        EntityEntry entry,
        Action<object> referenceAction,
        NavigationFilter? filter = null)
    {
        foreach (var navigation in NavigationPropertyHelper.GetReferenceNavigations(entry))
        {
            if (!ShouldTraverseReference(navigation, filter))
            {
                continue;
            }

            var refEntity = NavigationPropertyHelper.GetReferenceValue(navigation);
            if (refEntity != null)
            {
                referenceAction(refEntity);
            }
        }
    }

    /// <summary>
    /// Centralized check for whether a collection navigation should be traversed.
    /// Combines IsTraversableCollection + M2M check + filter check.
    /// </summary>
    internal static bool ShouldTraverseCollection(
        NavigationEntry navigation, NavigationFilter? filter, bool skipManyToMany = true)
    {
        if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
        {
            return false;
        }

        if (skipManyToMany && ManyToManyNavigationHelper.IsManyToManyNavigation(navigation))
        {
            return false;
        }

        return IsAllowedByFilter(navigation, filter);
    }

    /// <summary>
    /// Centralized check for whether a reference navigation should be traversed.
    /// </summary>
    internal static bool ShouldTraverseReference(
        NavigationEntry navigation, NavigationFilter? filter)
    {
        return IsAllowedByFilter(navigation, filter);
    }

    internal static bool IsAllowedByFilter(NavigationEntry navigation, NavigationFilter? filter)
    {
        if (filter == null)
        {
            return true;
        }

        var entityType = navigation.EntityEntry.Metadata.ClrType;
        return filter.ShouldTraverse(entityType, navigation.Metadata.Name);
    }
}
