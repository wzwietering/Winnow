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
    /// <param name="entry">The entity entry to traverse.</param>
    /// <param name="childAction">Action to invoke for each child.</param>
    /// <param name="skipManyToMany">When true, skips many-to-many navigations.</param>
    internal static void TraverseCollectionChildren(
        EntityEntry entry,
        Action<object> childAction,
        bool skipManyToMany = true)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            if (skipManyToMany && ManyToManyNavigationHelper.IsManyToManyNavigation(navigation))
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
    /// <param name="entry">The entity entry to traverse.</param>
    /// <param name="referenceAction">Action to invoke for each reference entity.</param>
    internal static void TraverseReferenceNavigations(
        EntityEntry entry,
        Action<object> referenceAction)
    {
        foreach (var navigation in NavigationPropertyHelper.GetReferenceNavigations(entry))
        {
            var refEntity = NavigationPropertyHelper.GetReferenceValue(navigation);
            if (refEntity != null)
            {
                referenceAction(refEntity);
            }
        }
    }
}
