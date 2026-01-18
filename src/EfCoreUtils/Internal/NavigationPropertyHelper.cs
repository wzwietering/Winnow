using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EfCoreUtils.Internal;

/// <summary>
/// Static utility methods for working with EF Core navigation properties.
/// </summary>
internal static class NavigationPropertyHelper
{
    internal static bool IsTraversableCollection(NavigationEntry navigation) => navigation.CurrentValue != null &&
               navigation.Metadata.IsCollection &&
               navigation.CurrentValue is System.Collections.IEnumerable;

    internal static IEnumerable<object> GetCollectionItems(NavigationEntry navigation) => 
        navigation.CurrentValue is System.Collections.IEnumerable collection ?
        collection.Cast<object>().ToList() :
        (IEnumerable<object>)[];

    internal static IProperty? GetForeignKeyProperty(NavigationEntry navigation) =>
        navigation.Metadata is INavigation navMetadata ?
        (navMetadata.ForeignKey?.Properties.FirstOrDefault()) :
        null;

    /// <summary>
    /// Returns true if the navigation is a reference (many-to-one), not a collection.
    /// </summary>
    internal static bool IsReferenceNavigation(NavigationEntry navigation) =>
        !navigation.Metadata.IsCollection && navigation.CurrentValue != null;

    /// <summary>
    /// Gets all reference navigations for an entity entry that have values.
    /// </summary>
    internal static IEnumerable<NavigationEntry> GetReferenceNavigations(EntityEntry entry) =>
        entry.Navigations.Where(IsReferenceNavigation);

    /// <summary>
    /// Gets the referenced entity value from a reference navigation.
    /// </summary>
    internal static object? GetReferenceValue(NavigationEntry navigation) =>
        navigation.Metadata.IsCollection ? null : navigation.CurrentValue;
}
