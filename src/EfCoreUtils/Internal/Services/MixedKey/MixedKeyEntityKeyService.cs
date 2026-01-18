using EfCoreUtils.MixedKey;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EfCoreUtils.Internal.Services.MixedKey;

/// <summary>
/// Extracts entity keys without TKey constraint, auto-detecting key types from EF Core metadata.
/// </summary>
internal class MixedKeyEntityKeyService
{
    private readonly DbContext _context;

    internal MixedKeyEntityKeyService(DbContext context)
    {
        _context = context;
    }

    internal MixedKeyId GetEntityKey(object entity)
    {
        var entry = _context.Entry(entity);
        return GetEntityKey(entry);
    }

    internal MixedKeyId GetEntityKey(EntityEntry entry)
    {
        var keyProperty = entry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (keyProperty == null)
        {
            throw new InvalidOperationException(
                $"Entity {entry.Metadata.ClrType.Name} does not have a primary key.");
        }

        var keyValue = entry.Property(keyProperty.Name).CurrentValue;
        if (keyValue == null)
        {
            throw new InvalidOperationException(
                $"Entity {entry.Metadata.ClrType.Name} has a null primary key value.");
        }

        return new MixedKeyId(keyValue, keyProperty.ClrType);
    }

    internal (string Type, MixedKeyId Id) CreateMixedEntityKey(EntityEntry entry)
    {
        return (entry.Metadata.ClrType.Name, GetEntityKey(entry));
    }
}
