using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Winnow.Internal;

/// <summary>
/// Static utility methods for working with EF Core entity entries.
/// </summary>
internal static class EntityEntryHelper
{
    /// <summary>
    /// Gets the primary key value of an entity safely, returning a unique reference-based
    /// identifier if the entity has no key or the key value is null.
    /// Returns CompositeKey for entities with composite primary keys.
    /// </summary>
    internal static object GetEntityIdSafe(EntityEntry entry)
    {
        var keyProperties = entry.Metadata.FindPrimaryKey()?.Properties;
        if (keyProperties == null || keyProperties.Count == 0)
        {
            return RuntimeHelpers.GetHashCode(entry.Entity);
        }

        if (keyProperties.Count == 1)
        {
            return GetSingleKeyValue(entry, keyProperties[0].Name);
        }

        return GetCompositeKeyValue(entry, keyProperties);
    }

    private static object GetSingleKeyValue(EntityEntry entry, string propertyName)
    {
        var value = entry.Property(propertyName).CurrentValue;
        return value ?? RuntimeHelpers.GetHashCode(entry.Entity);
    }

    private static CompositeKey GetCompositeKeyValue(
        EntityEntry entry,
        IReadOnlyList<Microsoft.EntityFrameworkCore.Metadata.IProperty> keyProperties)
    {
        var values = new object[keyProperties.Count];
        for (var i = 0; i < keyProperties.Count; i++)
        {
            var prop = keyProperties[i];
            var value = entry.Property(prop.Name).CurrentValue;
            if (value == null)
            {
                throw new InvalidOperationException(
                    $"Entity {entry.Metadata.ClrType.Name} has null value in primary key column '{prop.Name}'. " +
                    $"All composite key columns must have values.");
            }

            values[i] = value;
        }
        return new CompositeKey(values);
    }
}
