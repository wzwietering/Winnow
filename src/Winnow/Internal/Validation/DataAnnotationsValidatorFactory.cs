using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Winnow.Internal.Validation;

/// <summary>
/// Builds and caches DataAnnotations-driven <see cref="ValidatorDelegate{TEntity}"/>
/// instances. The first call for a given entity type reflects over its properties
/// to discover <see cref="ValidationAttribute"/>s; subsequent calls reuse the
/// cached <see cref="FrozenDictionary{TKey, TValue}"/> of (property accessor,
/// attribute array) pairs. The hot path is one dictionary lookup and a linear
/// walk over the cached attribute arrays — no reflection per entity.
/// </summary>
internal static class DataAnnotationsValidatorFactory
{
    private static readonly ConcurrentDictionary<Type, object> _cache = new();

    internal static ValidatorDelegate<TEntity> Create<TEntity>() where TEntity : class =>
        (ValidatorDelegate<TEntity>)_cache.GetOrAdd(typeof(TEntity), static t => Build<TEntity>());

    private static ValidatorDelegate<TEntity> Build<TEntity>() where TEntity : class
    {
        var properties = typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var entries = CollectAnnotatedProperties<TEntity>(properties);

        if (entries.Count == 0)
        {
            return static (TEntity _, ref ValidationCollector _) => { };
        }

        var frozen = entries.ToFrozenDictionary(e => e.Name, e => e);

        return (TEntity entity, ref ValidationCollector collector) =>
        {
            foreach (var entry in frozen.Values)
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

    private static Func<TEntity, object?> BuildGetter<TEntity>(PropertyInfo property) where TEntity : class
    {
        // Cache a compiled-ish getter once per type so per-entity invocation costs
        // a single delegate call rather than a full reflection round trip.
        return entity => property.GetValue(entity);
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
