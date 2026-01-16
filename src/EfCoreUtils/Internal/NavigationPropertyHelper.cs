using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EfCoreUtils.Internal;

/// <summary>
/// Static utility methods for working with EF Core navigation properties.
/// </summary>
internal static class NavigationPropertyHelper
{
    internal static bool IsTraversableCollection(NavigationEntry navigation)
    {
        return navigation.CurrentValue != null &&
               navigation.Metadata.IsCollection &&
               navigation.CurrentValue is System.Collections.IEnumerable;
    }

    internal static IEnumerable<object> GetCollectionItems(NavigationEntry navigation)
    {
        if (navigation.CurrentValue is System.Collections.IEnumerable collection)
        {
            return collection.Cast<object>().ToList();
        }
        return [];
    }

    internal static IProperty? GetForeignKeyProperty(NavigationEntry navigation)
    {
        if (navigation.Metadata is INavigation navMetadata)
        {
            return navMetadata.ForeignKey.Properties.FirstOrDefault();
        }
        return null;
    }
}
