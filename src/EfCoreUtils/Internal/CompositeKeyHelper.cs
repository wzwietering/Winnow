using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EfCoreUtils.Internal;

/// <summary>
/// Shared helper for extracting entity IDs (simple or composite) from entities and entries.
/// </summary>
internal static class CompositeKeyHelper
{
    internal static object? ExtractEntityId(EntityEntry entry, IReadOnlyList<IProperty> keyProperties)
    {
        if (keyProperties.Count == 1)
        {
            return entry.Property(keyProperties[0].Name).CurrentValue;
        }

        return ExtractCompositeKey(entry, keyProperties);
    }

    internal static object? ExtractEntityId(object item, IReadOnlyList<IProperty> keyProperties)
    {
        var itemType = item.GetType();

        if (keyProperties.Count == 1)
        {
            var prop = itemType.GetProperty(keyProperties[0].Name);
            return prop?.GetValue(item);
        }

        return ExtractCompositeKeyFromObject(item, itemType, keyProperties);
    }

    internal static bool IsCompatibleKeyType<TKey>(IReadOnlyList<IProperty>? keyProperties)
    {
        if (keyProperties == null || keyProperties.Count == 0)
        {
            return false;
        }

        if (keyProperties.Count == 1)
        {
            return keyProperties[0].ClrType == typeof(TKey);
        }

        return typeof(TKey) == typeof(CompositeKey);
    }

    internal static bool ForeignKeyMatchesParent<TKey>(
        EntityEntry entry, IReadOnlyList<IProperty> fkProperties, TKey parentId)
        where TKey : notnull
    {
        if (typeof(TKey) == typeof(CompositeKey) && fkProperties.Count > 1)
        {
            return MatchesCompositeParentKey(entry, fkProperties, parentId);
        }

        return MatchesSimpleParentKey(entry, fkProperties, parentId);
    }

    private static object? ExtractCompositeKey(EntityEntry entry, IReadOnlyList<IProperty> keyProperties)
    {
        var values = new object[keyProperties.Count];
        for (var i = 0; i < keyProperties.Count; i++)
        {
            var value = entry.Property(keyProperties[i].Name).CurrentValue;
            if (value == null)
            {
                return null;
            }

            values[i] = value;
        }
        return new CompositeKey(values);
    }

    private static object? ExtractCompositeKeyFromObject(
        object item, Type itemType, IReadOnlyList<IProperty> keyProperties)
    {
        var values = new object[keyProperties.Count];
        for (var i = 0; i < keyProperties.Count; i++)
        {
            var prop = itemType.GetProperty(keyProperties[i].Name);
            var value = prop?.GetValue(item);
            if (value == null)
            {
                return null;
            }

            values[i] = value;
        }
        return new CompositeKey(values);
    }

    private static bool MatchesCompositeParentKey<TKey>(
        EntityEntry entry, IReadOnlyList<IProperty> fkProperties, TKey parentId)
        where TKey : notnull
    {
        var fkValues = new object[fkProperties.Count];
        for (var i = 0; i < fkProperties.Count; i++)
        {
            var value = entry.Property(fkProperties[i].Name).CurrentValue;
            if (value == null)
            {
                return false;
            }

            fkValues[i] = value;
        }

        var fkKey = new CompositeKey(fkValues);
        if (parentId is CompositeKey parentCompositeKey)
        {
            return fkKey.Equals(parentCompositeKey);
        }

        return false;
    }

    private static bool MatchesSimpleParentKey<TKey>(
        EntityEntry entry, IReadOnlyList<IProperty> fkProperties, TKey parentId)
        where TKey : notnull
    {
        var fkValue = entry.Property(fkProperties[0].Name).CurrentValue;
        return fkValue is TKey key && key.Equals(parentId);
    }
}
