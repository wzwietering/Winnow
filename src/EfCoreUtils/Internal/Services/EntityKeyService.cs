using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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
        var keyProperty = entry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (keyProperty == null)
        {
            throw new InvalidOperationException("Entity does not have a primary key");
        }

        var keyValue = entry.Property(keyProperty.Name).CurrentValue;
        if (keyValue is TKey id)
        {
            return id;
        }

        throw new InvalidOperationException(
            $"Primary key type mismatch for entity {typeof(TEntity).Name}. " +
            $"Expected type {typeof(TKey).Name}, but entity has key type {keyProperty.ClrType.Name}. " +
            $"Use BatchSaver<{typeof(TEntity).Name}, {keyProperty.ClrType.Name}> instead.");
    }

    internal TKey GetEntityIdFromEntry(EntityEntry entry)
    {
        var keyProperty = entry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (keyProperty?.ClrType != typeof(TKey))
        {
            throw new InvalidOperationException(
                $"Entity {entry.Metadata.ClrType.Name} key type mismatch. Expected {typeof(TKey).Name}.");
        }

        var keyValue = entry.Property(keyProperty.Name).CurrentValue;
        if (keyValue is TKey id)
        {
            return id;
        }

        throw new InvalidOperationException(
            $"Could not retrieve key value for entity {entry.Metadata.ClrType.Name}.");
    }

    internal (string Type, TKey Id) CreateEntityKey(EntityEntry entry) => (entry.Metadata.ClrType.Name, GetEntityIdFromEntry(entry));
}
