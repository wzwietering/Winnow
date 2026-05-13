using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Winnow.Internal.Services;

internal class EntityKeyService<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly DbContext _context;

    internal EntityKeyService(DbContext context)
    {
        _context = context;
    }

    internal TKey GetEntityId(TEntity entity)
    {
        var entry = _context.Entry(entity);
        return GetEntityIdFromEntry(entry);
    }

    internal TKey GetEntityIdFromInstance(TEntity entity)
    {
        var entityType = _context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException(
                $"Entity type {typeof(TEntity).Name} is not part of the DbContext model.");
        var keyProperties = entityType.FindPrimaryKey()?.Properties
            ?? throw new InvalidOperationException(
                $"Entity {typeof(TEntity).Name} does not have a primary key.");

        if (keyProperties.Count == 1)
        {
            return ReadSimpleKey(entity, keyProperties[0]);
        }
        return ReadCompositeKey(entity, keyProperties);
    }

    private static TKey ReadSimpleKey(TEntity entity, IProperty keyProperty)
    {
        var propertyInfo = keyProperty.PropertyInfo
            ?? throw new InvalidOperationException(
                $"Primary key '{keyProperty.Name}' is a shadow property; MatchBy requires CLR properties.");
        var value = propertyInfo.GetValue(entity)
            ?? throw new InvalidOperationException(
                $"Entity {typeof(TEntity).Name} has null primary key value '{keyProperty.Name}'.");
        if (typeof(TKey) == typeof(CompositeKey))
        {
            return (TKey)(object)new CompositeKey(value);
        }
        return value is TKey typed
            ? typed
            : throw new InvalidOperationException(
                $"Primary key type mismatch on {typeof(TEntity).Name}. " +
                $"Expected {typeof(TKey).Name}, got {value.GetType().Name}.");
    }

    private static TKey ReadCompositeKey(TEntity entity, IReadOnlyList<IProperty> keyProperties)
    {
        if (typeof(TKey) != typeof(CompositeKey))
        {
            throw new InvalidOperationException(
                $"Entity {typeof(TEntity).Name} has a composite primary key but TKey is {typeof(TKey).Name}. " +
                $"Use CompositeKey to read this value.");
        }
        var values = new object[keyProperties.Count];
        for (var i = 0; i < keyProperties.Count; i++)
        {
            var propertyInfo = keyProperties[i].PropertyInfo
                ?? throw new InvalidOperationException(
                    $"Primary key '{keyProperties[i].Name}' is a shadow property; MatchBy requires CLR properties.");
            values[i] = propertyInfo.GetValue(entity)
                ?? throw new InvalidOperationException(
                    $"Entity {typeof(TEntity).Name} has null primary key column '{keyProperties[i].Name}'.");
        }
        return (TKey)(object)new CompositeKey(values);
    }

    internal void SetEntityId(TEntity entity, TKey value)
    {
        var entityType = _context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException(
                $"Entity type {typeof(TEntity).Name} is not part of the DbContext model.");
        var keyProperties = entityType.FindPrimaryKey()?.Properties
            ?? throw new InvalidOperationException(
                $"Entity {typeof(TEntity).Name} does not have a primary key.");

        if (keyProperties.Count == 1)
        {
            SetSimpleKey(entity, keyProperties[0], value);
            return;
        }
        SetCompositeKey(entity, keyProperties, value);
    }

    private static void SetSimpleKey(TEntity entity, IProperty keyProperty, TKey value)
    {
        var propertyInfo = keyProperty.PropertyInfo
            ?? throw new InvalidOperationException(
                $"Primary key '{keyProperty.Name}' on {typeof(TEntity).Name} is not a CLR property; " +
                $"shadow primary keys are not supported by MatchBy.");
        var scalar = value is CompositeKey ck && ck.IsSingle ? ck[0] : (object)value;
        propertyInfo.SetValue(entity, scalar);
    }

    private static void SetCompositeKey(TEntity entity, IReadOnlyList<IProperty> keyProperties, TKey value)
    {
        if (value is not CompositeKey composite)
        {
            throw new InvalidOperationException(
                $"Entity {typeof(TEntity).Name} has a composite primary key but TKey is {typeof(TKey).Name}. " +
                $"Use a CompositeKey to set this value.");
        }
        if (composite.Count != keyProperties.Count)
        {
            throw new InvalidOperationException(
                $"CompositeKey has {composite.Count} component(s) but entity {typeof(TEntity).Name} " +
                $"has {keyProperties.Count} primary key column(s).");
        }
        for (var i = 0; i < keyProperties.Count; i++)
        {
            var propertyInfo = keyProperties[i].PropertyInfo
                ?? throw new InvalidOperationException(
                    $"Primary key '{keyProperties[i].Name}' is a shadow property; MatchBy does not support this.");
            propertyInfo.SetValue(entity, composite[i]);
        }
    }

    internal TKey GetEntityIdFromEntry(EntityEntry entry)
    {
        var keyProperties = GetKeyProperties(entry);

        if (keyProperties.Count == 1)
        {
            return GetSimpleKey(entry, keyProperties[0]);
        }

        return GetCompositeKey(entry, keyProperties);
    }

    internal (string Type, TKey Id) CreateEntityKey(EntityEntry entry) =>
        (entry.Metadata.ClrType.Name, GetEntityIdFromEntry(entry));

    private static IReadOnlyList<IProperty> GetKeyProperties(EntityEntry entry)
    {
        var keyProperties = entry.Metadata.FindPrimaryKey()?.Properties;
        if (keyProperties == null || keyProperties.Count == 0)
        {
            throw new InvalidOperationException($"Entity {entry.Metadata.ClrType.Name} does not have a primary key");
        }

        return keyProperties;
    }

    private static TKey GetSimpleKey(EntityEntry entry, IProperty keyProperty)
    {
        var keyValue = entry.Property(keyProperty.Name).CurrentValue;

        return IsAutoDetectMode()
            ? WrapInCompositeKey(entry, keyValue)
            : GetTypedKey(entry, keyValue, keyProperty);
    }

    private static bool IsAutoDetectMode() => typeof(TKey) == typeof(CompositeKey);

    private static TKey WrapInCompositeKey(EntityEntry entry, object? keyValue)
    {
        if (keyValue == null)
        {
            throw new InvalidOperationException(
                $"Entity {entry.Metadata.ClrType.Name} has null primary key value.");
        }

        return (TKey)(object)new CompositeKey(keyValue);
    }

    private static TKey GetTypedKey(EntityEntry entry, object? keyValue, IProperty keyProperty)
    {
        if (keyValue is TKey id) return id;

        throw new InvalidOperationException(
            $"Primary key type mismatch for entity {entry.Metadata.ClrType.Name}. " +
            $"Expected type {typeof(TKey).Name}, but entity has key type {keyProperty.ClrType.Name}. " +
            $"Use Winnower<{entry.Metadata.ClrType.Name}, {keyProperty.ClrType.Name}> instead.");
    }

    private static TKey GetCompositeKey(EntityEntry entry, IReadOnlyList<IProperty> keyProperties)
    {
        ValidateCompositeKeyType(entry, keyProperties);
        return CreateCompositeKeyResult(entry, keyProperties);
    }

    private static void ValidateCompositeKeyType(EntityEntry entry, IReadOnlyList<IProperty> keyProperties)
    {
        if (typeof(TKey) == typeof(CompositeKey)) return;

        var keyTypes = string.Join(", ", keyProperties.Select(p => $"{p.Name}: {p.ClrType.Name}"));
        throw new InvalidOperationException(
            $"Entity {entry.Metadata.ClrType.Name} has composite primary key ({keyTypes}), " +
            $"but Winnower was configured with key type {typeof(TKey).Name}. " +
            $"Use Winnower<{entry.Metadata.ClrType.Name}> for auto-detection, or " +
            $"Winnower<{entry.Metadata.ClrType.Name}, CompositeKey> for explicit typing.");
    }

    private static TKey CreateCompositeKeyResult(EntityEntry entry, IReadOnlyList<IProperty> keyProperties)
    {
        var values = ExtractKeyValues(entry, keyProperties);
        var compositeKey = new CompositeKey(values);

        return compositeKey is TKey result
            ? result
            : throw new InvalidOperationException($"Failed to create composite key for {entry.Metadata.ClrType.Name}");
    }

    private static object[] ExtractKeyValues(EntityEntry entry, IReadOnlyList<IProperty> keyProperties)
    {
        var values = new object[keyProperties.Count];
        for (var i = 0; i < keyProperties.Count; i++)
        {
            var prop = keyProperties[i];
            var value = entry.Property(prop.Name).CurrentValue;
            values[i] = value ?? throw new InvalidOperationException(
                $"Entity {entry.Metadata.ClrType.Name} has null value in primary key column '{prop.Name}'. " +
                $"All composite key columns must have values.");
        }
        return values;
    }
}
