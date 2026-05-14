using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Winnow.Internal.Validation;

/// <summary>
/// Walks an entity's reference and collection navigation properties, applying
/// the cached DataAnnotations validator to each reachable child whose type has
/// any annotated properties. Used by <see cref="PreValidationRunner"/> when
/// <see cref="ValidationOptions.IncludeNavigations"/> is set on a
/// DataAnnotations-built validator.
/// </summary>
/// <remarks>
/// <para>
/// Navigation discovery is reflection-based and does not consult the EF Core
/// model. A property is treated as a navigation if its value type is a class
/// (excluding <see cref="string"/>) that has DataAnnotations attributes, or an
/// <see cref="IEnumerable"/> whose element type satisfies the same rule. Types
/// with no DataAnnotations are skipped entirely — there is nothing to validate.
/// </para>
/// <para>
/// Cycle protection uses reference equality so self-referencing graphs (parent
/// → child → parent) terminate after one visit.
/// </para>
/// </remarks>
internal static class NavigationWalker
{
    private static readonly ConcurrentDictionary<Type, NavigationProperty[]> _navCache = new();

    /// <summary>
    /// Walks <paramref name="root"/>'s reachable children and adds any validation
    /// failures to <paramref name="collector"/> with property paths prefixed so
    /// the failing entity can be located in the graph. When <paramref name="filter"/>
    /// is supplied, navigations excluded by the filter are skipped, matching the
    /// scope of the graph operation that owns this walk.
    /// </summary>
    internal static void Walk(object root, ref ValidationCollector collector, NavigationFilter? filter = null)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { root };
        WalkChildrenOf(root, ref collector, visited, filter, pathPrefix: string.Empty);
    }

    private static void WalkChildrenOf(
        object entity, ref ValidationCollector collector, HashSet<object> visited,
        NavigationFilter? filter, string pathPrefix)
    {
        var entityType = entity.GetType();
        var navs = GetNavigations(entityType);
        foreach (var nav in navs)
        {
            if (filter is not null && !filter.ShouldTraverse(entityType, nav.Name)) continue;
            var value = nav.Getter(entity);
            if (value is null) continue;
            if (nav.IsCollection)
            {
                WalkCollection((IEnumerable)value, nav.Name, ref collector, visited, filter, pathPrefix);
            }
            else
            {
                WalkSingle(value, nav.Name, ref collector, visited, filter, pathPrefix);
            }
        }
    }

    private static void WalkSingle(
        object child, string navName, ref ValidationCollector collector,
        HashSet<object> visited, NavigationFilter? filter, string pathPrefix)
    {
        if (!visited.Add(child)) return;
        var childPrefix = pathPrefix + navName + ".";
        DataAnnotationsValidatorFactory.ValidateInstance(child, ref collector, childPrefix);
        WalkChildrenOf(child, ref collector, visited, filter, childPrefix);
    }

    private static void WalkCollection(
        IEnumerable collection, string navName, ref ValidationCollector collector,
        HashSet<object> visited, NavigationFilter? filter, string pathPrefix)
    {
        int index = 0;
        foreach (var child in collection)
        {
            if (child is null) { index++; continue; }
            if (visited.Add(child))
            {
                var childPrefix = $"{pathPrefix}{navName}[{index}].";
                DataAnnotationsValidatorFactory.ValidateInstance(child, ref collector, childPrefix);
                WalkChildrenOf(child, ref collector, visited, filter, childPrefix);
            }
            index++;
        }
    }

    private static NavigationProperty[] GetNavigations(Type type) =>
        _navCache.GetOrAdd(type, static t => BuildNavigations(t));

    private static NavigationProperty[] BuildNavigations(Type type)
    {
        var list = new List<NavigationProperty>();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || IsScalar(property.PropertyType)) continue;
            var classified = ClassifyNavigation(property);
            if (classified is { } nav) list.Add(nav);
        }
        return list.ToArray();
    }

    private static NavigationProperty? ClassifyNavigation(PropertyInfo property)
    {
        var elementType = TryGetCollectionElementType(property.PropertyType);
        if (elementType is not null)
        {
            return TypeHasReachableAnnotations(elementType, new HashSet<Type>())
                ? new NavigationProperty(property.Name, BuildGetter(property), IsCollection: true)
                : null;
        }
        return TypeHasReachableAnnotations(property.PropertyType, new HashSet<Type>())
            ? new NavigationProperty(property.Name, BuildGetter(property), IsCollection: false)
            : null;
    }

    private static readonly ConcurrentDictionary<Type, bool> _reachableCache = new();

    /// <summary>
    /// True if <paramref name="type"/> has its own DataAnnotations OR any class-typed
    /// navigation reachable from it transitively does. The intermediate type itself
    /// need not be annotated — failing to recurse through it would silently drop the
    /// reachable leaf's failures.
    /// </summary>
    private static bool TypeHasReachableAnnotations(Type type, HashSet<Type> inProgress)
    {
        if (_reachableCache.TryGetValue(type, out var cached)) return cached;
        if (TypeHasAnnotations(type))
        {
            _reachableCache[type] = true;
            return true;
        }
        if (!inProgress.Add(type)) return false;
        try
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || IsScalar(property.PropertyType)) continue;
                var childType = TryGetCollectionElementType(property.PropertyType) ?? property.PropertyType;
                if (TypeHasReachableAnnotations(childType, inProgress))
                {
                    _reachableCache[type] = true;
                    return true;
                }
            }
            _reachableCache[type] = false;
            return false;
        }
        finally
        {
            inProgress.Remove(type);
        }
    }

    private static bool IsScalar(Type type) =>
        type.IsValueType || type == typeof(string);

    private static Type? TryGetCollectionElementType(Type type)
    {
        if (type == typeof(string)) return null;
        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                var element = iface.GetGenericArguments()[0];
                if (!element.IsValueType && element != typeof(string)) return element;
            }
        }
        return null;
    }

    private static bool TypeHasAnnotations(Type type) =>
        DataAnnotationsValidatorFactory.GetUntypedEntries(type).Length > 0;

    private static Func<object, object?> BuildGetter(PropertyInfo property)
    {
        var param = Expression.Parameter(typeof(object), "entity");
        var cast = Expression.Convert(param, property.DeclaringType!);
        var propertyAccess = Expression.Property(cast, property);
        Expression body = property.PropertyType.IsValueType
            ? Expression.Convert(propertyAccess, typeof(object))
            : propertyAccess;
        return Expression.Lambda<Func<object, object?>>(body, param).Compile();
    }

    private readonly record struct NavigationProperty(string Name, Func<object, object?> Getter, bool IsCollection);
}
