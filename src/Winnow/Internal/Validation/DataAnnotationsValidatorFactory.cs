using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;

namespace Winnow.Internal.Validation;

/// <summary>
/// Builds and caches DataAnnotations-driven <see cref="ValidatorDelegate{TEntity}"/>
/// instances. The first call for a given entity type reflects over its members to
/// discover property-level <see cref="ValidationAttribute"/>s, class-level attributes
/// (e.g. <see cref="CustomValidationAttribute"/>), and any <see cref="IValidatableObject"/>
/// implementation. Subsequent calls reuse the cached metadata; the per-entity hot
/// path is a linear walk over compiled getters with no per-call reflection.
/// </summary>
internal static class DataAnnotationsValidatorFactory
{
    /// <summary>
    /// Code applied to errors emitted by <see cref="IValidatableObject.Validate"/>
    /// — distinguishes them from property-attribute failures (whose code is the
    /// attribute type name, e.g. <c>RequiredAttribute</c>). Alias for the public
    /// <see cref="ValidationErrorCodes.ValidatableObject"/>.
    /// </summary>
    internal const string ValidatableObjectErrorCode = ValidationErrorCodes.ValidatableObject;

    private static readonly ConcurrentDictionary<Type, object> _cache = new();
    private static readonly ConcurrentDictionary<Type, UntypedAnnotatedProperty[]> _untypedCache = new();
    private static readonly ConcurrentDictionary<Type, TypeLevelMetadata> _typeMetadataCache = new();

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
        ValidatePropertyAttributes(entity, ref collector, pathPrefix);
        var typeMeta = GetTypeMetadata(entity.GetType());
        ValidateClassLevelAttributes(entity, typeMeta.ClassAttributes, ref collector, pathPrefix);
        ValidateIValidatableObject(entity, typeMeta.IsValidatableObject, ref collector, pathPrefix);
    }

    private static void ValidatePropertyAttributes(
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

    private static void ValidateClassLevelAttributes(
        object entity, ValidationAttribute[] classAttributes,
        ref ValidationCollector collector, string pathPrefix)
    {
        if (classAttributes.Length == 0) return;
        var context = new ValidationContext(entity);
        foreach (var attribute in classAttributes)
        {
            var result = attribute.GetValidationResult(entity, context);
            if (result is null) continue;
            AddResult(result, attribute.GetType().Name, ref collector, pathPrefix);
        }
    }

    private static void ValidateIValidatableObject(
        object entity, bool isValidatableObject,
        ref ValidationCollector collector, string pathPrefix)
    {
        if (!isValidatableObject) return;
        var context = new ValidationContext(entity);
        foreach (var result in ((IValidatableObject)entity).Validate(context))
        {
            AddResult(result, ValidatableObjectErrorCode, ref collector, pathPrefix);
        }
    }

    private static void AddResult(
        ValidationResult result, string code,
        ref ValidationCollector collector, string pathPrefix)
    {
        var message = result.ErrorMessage ?? string.Empty;
        var members = result.MemberNames as ICollection<string> ?? result.MemberNames.ToArray();
        if (members.Count == 0)
        {
            collector.Add(new ValidationError(pathPrefix, message, code));
            return;
        }
        foreach (var member in members)
        {
            collector.Add(new ValidationError(pathPrefix + (member ?? string.Empty), message, code));
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

    private static TypeLevelMetadata GetTypeMetadata(Type type) =>
        _typeMetadataCache.GetOrAdd(type, static t => BuildTypeMetadata(t));

    private static TypeLevelMetadata BuildTypeMetadata(Type type)
    {
        var classAttributes = type.GetCustomAttributes<ValidationAttribute>(inherit: true).ToArray();
        var isValidatable = typeof(IValidatableObject).IsAssignableFrom(type);
        return new TypeLevelMetadata(classAttributes, isValidatable);
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

    private readonly record struct TypeLevelMetadata(
        ValidationAttribute[] ClassAttributes,
        bool IsValidatableObject);

    private static ValidatorDelegate<TEntity> Build<TEntity>() where TEntity : class
    {
        var properties = typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var entries = CollectAnnotatedProperties<TEntity>(properties);
        var typeMeta = GetTypeMetadata(typeof(TEntity));

        if (entries.Count == 0 && typeMeta.ClassAttributes.Length == 0 && !typeMeta.IsValidatableObject)
        {
            return static (TEntity _, ref ValidationCollector _) => { };
        }

        var frozen = entries.ToArray();
        var classAttrs = typeMeta.ClassAttributes;
        var isValidatable = typeMeta.IsValidatableObject;

        return (TEntity entity, ref ValidationCollector collector) =>
        {
            ValidatePropertyEntries(entity, frozen, ref collector);
            ValidateClassLevelAttributes(entity, classAttrs, ref collector, pathPrefix: string.Empty);
            ValidateIValidatableObject(entity, isValidatable, ref collector, pathPrefix: string.Empty);
        };
    }

    private static void ValidatePropertyEntries<TEntity>(
        TEntity entity, AnnotatedProperty<TEntity>[] entries, ref ValidationCollector collector)
        where TEntity : class
    {
        foreach (var entry in entries)
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
