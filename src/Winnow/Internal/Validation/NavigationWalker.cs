using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Winnow.Internal.Validation;

/// <summary>
/// Walks an entity's reference and collection navigation properties, applying
/// the cached DataAnnotations validator to each reachable child whose type has
/// any annotated properties. Used by <see cref="PreValidationRunner"/> when
/// <see cref="GraphValidationOptions.IncludeNavigations"/> is set on a
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
    /// Error code emitted when the navigation walk reaches
    /// <see cref="GraphValidationOptions.MaxNavigationDepth"/> and stops descending
    /// further. Distinguishable from real validation failures so callers can
    /// surface a depth-limit hit as a configuration issue rather than bad data.
    /// </summary>
    internal const string DepthLimitErrorCode = "WINNOW_NAV_DEPTH_LIMIT";

    /// <summary>
    /// Walks <paramref name="root"/>'s reachable children and adds any validation
    /// failures to <paramref name="collector"/> with property paths prefixed so
    /// the failing entity can be located in the graph. When <paramref name="filter"/>
    /// is supplied, navigations excluded by the filter are skipped, matching the
    /// scope of the graph operation that owns this walk. The walker stops
    /// descending when <paramref name="maxDepth"/> is reached and records a
    /// <see cref="DepthLimitErrorCode"/> error at the cut-off point — this is
    /// what keeps deeply-nested or accidentally-unbounded graphs from blowing
    /// the stack.
    /// </summary>
    internal static void Walk(object root, ref ValidationCollector collector, int maxDepth, NavigationFilter? filter = null)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { root };
        WalkChildrenOf(root, ref collector, visited, filter, pathPrefix: string.Empty, depth: 0, maxDepth);
    }

    private static void WalkChildrenOf(
        object entity, ref ValidationCollector collector, HashSet<object> visited,
        NavigationFilter? filter, string pathPrefix, int depth, int maxDepth)
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
                WalkCollection((IEnumerable)value, nav.Name, ref collector, visited, filter, pathPrefix, depth, maxDepth);
            }
            else
            {
                WalkSingle(value, nav.Name, ref collector, visited, filter, pathPrefix, depth, maxDepth);
            }
        }
    }

    private static void WalkSingle(
        object child, string navName, ref ValidationCollector collector,
        HashSet<object> visited, NavigationFilter? filter, string pathPrefix, int depth, int maxDepth)
    {
        if (!visited.Add(child)) return;
        var childPrefix = pathPrefix + navName + ".";
        DataAnnotationsValidatorFactory.ValidateInstance(child, ref collector, childPrefix);
        DescendOrFlag(child, ref collector, visited, filter, childPrefix, depth, maxDepth);
    }

    private static void WalkCollection(
        IEnumerable collection, string navName, ref ValidationCollector collector,
        HashSet<object> visited, NavigationFilter? filter, string pathPrefix, int depth, int maxDepth)
    {
        int index = 0;
        foreach (var child in collection)
        {
            if (child is null) { index++; continue; }
            if (visited.Add(child))
            {
                var childPrefix = $"{pathPrefix}{navName}[{index}].";
                DataAnnotationsValidatorFactory.ValidateInstance(child, ref collector, childPrefix);
                DescendOrFlag(child, ref collector, visited, filter, childPrefix, depth, maxDepth);
            }
            index++;
        }
    }

    private static void DescendOrFlag(
        object child, ref ValidationCollector collector, HashSet<object> visited,
        NavigationFilter? filter, string childPrefix, int depth, int maxDepth)
    {
        var nextDepth = depth + 1;
        if (nextDepth >= maxDepth)
        {
            collector.Add(new ValidationError(
                TrimTrailingDot(childPrefix),
                $"Navigation depth limit ({maxDepth}) reached; descendants of this entity were not validated.",
                DepthLimitErrorCode));
            return;
        }
        WalkChildrenOf(child, ref collector, visited, filter, childPrefix, nextDepth, maxDepth);
    }

    private static string TrimTrailingDot(string path) =>
        path.Length > 0 && path[^1] == '.' ? path[..^1] : path;

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
        var childType = elementType ?? property.PropertyType;
        return HasAnyReachableAnnotation(childType)
            ? new NavigationProperty(property.Name, BuildGetter(property), IsCollection: elementType is not null)
            : null;
    }

    // Cache only positive ("has reachable annotations") results. Negative results
    // discovered through a cycle bail-out are not authoritative — a non-cyclic
    // visit through a different ancestor could still find an annotation — so
    // caching them risks permanently masking children that should be validated.
    // Recomputing the negative case stays cheap because _navCache memoises the
    // outer BuildNavigations result, so each type is walked once per process.
    private static readonly ConcurrentDictionary<Type, bool> _reachableCache = new();

    private static bool HasAnyReachableAnnotation(Type type) =>
        TypeHasReachableAnnotations(type, new HashSet<Type>());

    /// <summary>
    /// True if <paramref name="type"/> has its own DataAnnotations OR any class-typed
    /// navigation reachable from it transitively does. The intermediate type itself
    /// need not be annotated — failing to recurse through it would silently drop the
    /// reachable leaf's failures. Only positive results are cached; see
    /// <see cref="_reachableCache"/>.
    /// </summary>
    private static bool TypeHasReachableAnnotations(Type type, HashSet<Type> inProgress)
    {
        if (_reachableCache.TryGetValue(type, out var cached)) return cached;
        if (TypeHasAnnotations(type))
        {
            _reachableCache.TryAdd(type, true);
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
                    _reachableCache.TryAdd(type, true);
                    return true;
                }
            }
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
