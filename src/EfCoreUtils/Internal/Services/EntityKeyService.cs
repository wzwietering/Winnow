using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EfCoreUtils.Internal.Services;

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
            $"Use BatchSaver<{entry.Metadata.ClrType.Name}, {keyProperty.ClrType.Name}> instead.");
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
            $"but BatchSaver was configured with key type {typeof(TKey).Name}. " +
            $"Use BatchSaver<{entry.Metadata.ClrType.Name}> for auto-detection, or " +
            $"BatchSaver<{entry.Metadata.ClrType.Name}, CompositeKey> for explicit typing.");
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
