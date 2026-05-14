using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;

namespace Winnow.Internal.Validation;

/// <summary>
/// Builds and caches DataAnnotations-driven <see cref="ValidatorDelegate{TEntity}"/>
/// instances. The first call for a given entity type reflects over its properties
/// to discover <see cref="ValidationAttribute"/>s; subsequent calls reuse the
/// cached array of (compiled getter, attribute array) pairs. The hot path is a
/// linear walk over the array with no reflection per entity — each getter is a
/// compiled expression tree that calls the property's get method directly.
/// </summary>
internal static class DataAnnotationsValidatorFactory
{
    private static readonly ConcurrentDictionary<Type, object> _cache = new();
    private static readonly ConcurrentDictionary<Type, UntypedAnnotatedProperty[]> _untypedCache = new();

    internal static ValidatorDelegate<TEntity> Create<TEntity>() where TEntity : class =>
        (ValidatorDelegate<TEntity>)_cache.GetOrAdd(typeof(TEntity), static t => Build<TEntity>());

    /// <summary>
    /// Validates an entity polymorphically based on its runtime type. Used by
    /// <see cref="NavigationWalker"/> to apply DataAnnotations to children whose
    /// types are not known at compile time. Each error's <c>PropertyName</c> is
    /// prefixed with <paramref name="pathPrefix"/> (e.g. <c>"Items[2]."</c>).
    /// </summary>
    internal static void ValidateInstance(
        object entity, ref ValidationCollector collector, string pathPrefix)
    {
        var entries = GetUntypedEntries(entity.GetType());
        foreach (var entry in entries)
        {
            var value = entry.Getter(entity);
            foreach (var attribute in entry.Attributes)
            {
                if (!attribute.IsValid(value))
                {
                    collector.Add(new ValidationError(
                        pathPrefix + entry.Name,
                        attribute.FormatErrorMessage(entry.Name),
                        attribute.GetType().Name));
                }
            }
        }
    }

    internal static UntypedAnnotatedProperty[] GetUntypedEntries(Type type) =>
        _untypedCache.GetOrAdd(type, static t => BuildUntyped(t));

    private static UntypedAnnotatedProperty[] BuildUntyped(Type type)
    {
        var list = new List<UntypedAnnotatedProperty>();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead) continue;
            var attributes = property.GetCustomAttributes<ValidationAttribute>(inherit: true).ToArray();
            if (attributes.Length == 0) continue;
            list.Add(new UntypedAnnotatedProperty(property.Name, BuildUntypedGetter(type, property), attributes));
        }
        return list.ToArray();
    }

    private static Func<object, object?> BuildUntypedGetter(Type entityType, PropertyInfo property)
    {
        var param = Expression.Parameter(typeof(object), "entity");
        var cast = Expression.Convert(param, entityType);
        var propertyAccess = Expression.Property(cast, property);
        var asObject = property.PropertyType.IsValueType
            ? (Expression)Expression.Convert(propertyAccess, typeof(object))
            : propertyAccess;
        return Expression.Lambda<Func<object, object?>>(asObject, param).Compile();
    }

    internal readonly struct UntypedAnnotatedProperty(
        string name,
        Func<object, object?> getter,
        ValidationAttribute[] attributes)
    {
        public string Name { get; } = name;
        public ValidationAttribute[] Attributes { get; } = attributes;
        public Func<object, object?> Getter { get; } = getter;
    }

    private static ValidatorDelegate<TEntity> Build<TEntity>() where TEntity : class
    {
        var properties = typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var entries = CollectAnnotatedProperties<TEntity>(properties);

        if (entries.Count == 0)
        {
            return static (TEntity _, ref ValidationCollector _) => { };
        }

        var frozen = entries.ToArray();

        return (TEntity entity, ref ValidationCollector collector) =>
        {
            foreach (var entry in frozen)
            {
                var value = entry.GetValue(entity);
                foreach (var attribute in entry.Attributes)
                {
                    if (!attribute.IsValid(value))
                    {
                        collector.Add(new ValidationError(
                            entry.Name,
                            attribute.FormatErrorMessage(entry.Name),
                            attribute.GetType().Name));
                    }
                }
            }
        };
    }

    private static List<AnnotatedProperty<TEntity>> CollectAnnotatedProperties<TEntity>(PropertyInfo[] properties)
        where TEntity : class
    {
        var list = new List<AnnotatedProperty<TEntity>>();
        foreach (var property in properties)
        {
            if (!property.CanRead)
            {
                continue;
            }
            var attributes = property.GetCustomAttributes<ValidationAttribute>(inherit: true).ToArray();
            if (attributes.Length == 0)
            {
                continue;
            }
            list.Add(new AnnotatedProperty<TEntity>(property.Name, BuildGetter<TEntity>(property), attributes));
        }
        return list;
    }

    internal static Func<TEntity, object?> BuildGetter<TEntity>(PropertyInfo property) where TEntity : class
    {
        // Compile an expression tree once per (TEntity, property) pair so the per-entity
        // hot path is a direct virtual call to the property's get method — no
        // PropertyInfo.GetValue / MethodBase.Invoke reflection on each call.
        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var propertyAccess = Expression.Property(parameter, property);
        var asObject = property.PropertyType.IsValueType
            ? (Expression)Expression.Convert(propertyAccess, typeof(object))
            : propertyAccess;
        return Expression.Lambda<Func<TEntity, object?>>(asObject, parameter).Compile();
    }

    private readonly struct AnnotatedProperty<TEntity>(
        string name,
        Func<TEntity, object?> getValue,
        ValidationAttribute[] attributes)
        where TEntity : class
    {
        public string Name { get; } = name;
        public ValidationAttribute[] Attributes { get; } = attributes;
        public object? GetValue(TEntity entity) => getValue(entity);
    }
}
