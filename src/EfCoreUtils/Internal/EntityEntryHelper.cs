using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EfCoreUtils.Internal;

/// <summary>
/// Static utility methods for working with EF Core entity entries.
/// </summary>
internal static class EntityEntryHelper
{
    /// <summary>
    /// Gets the primary key value of an entity safely, returning a unique reference-based
    /// identifier if the entity has no key or the key value is null.
    /// </summary>
    internal static object GetEntityIdSafe(EntityEntry entry)
    {
        var keyProperty = entry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (keyProperty == null)
        {
            return RuntimeHelpers.GetHashCode(entry.Entity);
        }

        var value = entry.Property(keyProperty.Name).CurrentValue;
        return value ?? RuntimeHelpers.GetHashCode(entry.Entity);
    }
}
