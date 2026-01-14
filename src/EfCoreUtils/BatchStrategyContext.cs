using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils;

internal class BatchStrategyContext<TEntity> where TEntity : class
{
    private readonly DbContext _context;
    private int _roundTripCounter;

    internal BatchStrategyContext(DbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _roundTripCounter = 0;
    }

    internal DbContext Context => _context;
    internal int RoundTripCounter => _roundTripCounter;
    internal void IncrementRoundTrip() => _roundTripCounter++;

    internal int GetEntityId(TEntity entity)
    {
        var entry = _context.Entry(entity);
        var keyProperty = entry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (keyProperty == null)
        {
            throw new InvalidOperationException("Entity does not have a primary key");
        }

        var keyValue = entry.Property(keyProperty.Name).CurrentValue;
        if (keyValue is int id)
        {
            return id;
        }

        throw new InvalidOperationException("Primary key must be of type int");
    }

    internal BatchFailure CreateBatchFailure(int entityId, Exception exception)
    {
        var reason = exception switch
        {
            InvalidOperationException => FailureReason.ValidationError,
            DbUpdateConcurrencyException => FailureReason.ConcurrencyConflict,
            DbUpdateException => FailureReason.DatabaseConstraint,
            _ => FailureReason.UnknownError
        };

        return new BatchFailure
        {
            EntityId = entityId,
            ErrorMessage = exception.Message,
            Reason = reason,
            Exception = exception
        };
    }

    internal void DetachEntity(TEntity entity)
    {
        var entry = _context.Entry(entity);
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Detached;
        }
    }

    internal void DetachAllEntities(List<TEntity> entities)
    {
        foreach (var entity in entities)
        {
            DetachEntityGraph(entity);
        }
    }

    private void DetachEntityGraph(TEntity entity)
    {
        var entry = _context.Entry(entity);

        // Detach navigation properties first
        foreach (var navigation in entry.Navigations)
        {
            if (navigation.CurrentValue != null)
            {
                if (navigation.Metadata.IsCollection)
                {
                    if (navigation.CurrentValue is System.Collections.IEnumerable collection)
                    {
                        foreach (var item in collection)
                        {
                            var itemEntry = _context.Entry(item);
                            if (itemEntry.State != EntityState.Detached)
                            {
                                itemEntry.State = EntityState.Detached;
                            }
                        }
                    }
                }
                else
                {
                    var navEntry = _context.Entry(navigation.CurrentValue);
                    if (navEntry.State != EntityState.Detached)
                    {
                        navEntry.State = EntityState.Detached;
                    }
                }
            }
        }

        // Then detach the parent entity
        DetachEntity(entity);
    }

    internal void ValidateNoModifiedNavigationProperties(TEntity entity)
    {
        var entry = _context.Entry(entity);
        var modifiedNavigations = new List<string>();

        foreach (var navigation in entry.Navigations)
        {
            if (navigation.CurrentValue == null)
            {
                continue;
            }

            // Check collection navigations
            if (navigation.Metadata.IsCollection)
            {
                if (navigation.CurrentValue is System.Collections.IEnumerable collection)
                {
                    foreach (var item in collection)
                    {
                        var itemEntry = _context.Entry(item);
                        if (itemEntry.State == EntityState.Added ||
                            itemEntry.State == EntityState.Deleted ||
                            itemEntry.State == EntityState.Modified)
                        {
                            modifiedNavigations.Add($"{navigation.Metadata.Name} (collection items)");
                            break;
                        }
                    }
                }
            }
            else
            {
                // Check reference navigation
                var navEntry = _context.Entry(navigation.CurrentValue);
                if (navEntry.State == EntityState.Modified ||
                    navEntry.State == EntityState.Added ||
                    navEntry.State == EntityState.Deleted)
                {
                    modifiedNavigations.Add(navigation.Metadata.Name);
                }
            }
        }

        if (modifiedNavigations.Count != 0)
        {
            var entityId = GetEntityId(entity);
            throw new InvalidOperationException(
                $"Entity {typeof(TEntity).Name} (Id={entityId}) has modified navigation properties: " +
                $"{string.Join(", ", modifiedNavigations)}. " +
                $"BatchSaver<{typeof(TEntity).Name}> only updates parent entities. " +
                $"To update entity graphs, use standard EF Core SaveChanges() or set " +
                $"BatchOptions.ValidateNavigationProperties = false to suppress this check.");
        }
    }

    // ========== Graph Update Methods (Phase 2) ==========

    internal void AttachEntityGraphAsModified(TEntity entity)
    {
        var entry = _context.Entry(entity);
        entry.State = EntityState.Modified;

        foreach (var navigation in entry.Navigations)
        {
            if (navigation.CurrentValue == null || !navigation.Metadata.IsCollection)
            {
                continue;
            }

            AttachCollectionChildrenAsModified(navigation);
        }
    }

    private void AttachCollectionChildrenAsModified(
        Microsoft.EntityFrameworkCore.ChangeTracking.NavigationEntry navigation)
    {
        if (navigation.CurrentValue is not System.Collections.IEnumerable collection)
        {
            return;
        }

        foreach (var item in collection)
        {
            var itemEntry = _context.Entry(item);
            if (itemEntry.State == EntityState.Detached)
            {
                itemEntry.State = EntityState.Modified;
            }
        }
    }

    internal List<int> GetChildIds(TEntity entity)
    {
        var childIds = new List<int>();
        var entry = _context.Entry(entity);

        foreach (var navigation in entry.Navigations)
        {
            if (navigation.CurrentValue == null || !navigation.Metadata.IsCollection)
            {
                continue;
            }

            AddChildIdsFromCollection(navigation, childIds);
        }

        return childIds;
    }

    private void AddChildIdsFromCollection(
        Microsoft.EntityFrameworkCore.ChangeTracking.NavigationEntry navigation,
        List<int> childIds)
    {
        if (navigation.CurrentValue is not System.Collections.IEnumerable collection)
        {
            return;
        }

        foreach (var item in collection)
        {
            var itemEntry = _context.Entry(item);
            var keyProperty = itemEntry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
            if (keyProperty?.ClrType == typeof(int))
            {
                var keyValue = itemEntry.Property(keyProperty.Name).CurrentValue;
                if (keyValue is int id)
                {
                    childIds.Add(id);
                }
            }
        }
    }
}
