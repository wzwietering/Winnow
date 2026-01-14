using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils;

internal class BatchStrategyContext<TEntity> where TEntity : class
{
    private readonly DbContext _context;
    private int _roundTripCounter;
    private readonly Dictionary<int, HashSet<int>> _originalChildIdsByParent = [];
    private readonly Dictionary<int, List<object>> _deletedChildrenByParent = [];

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

    internal void DetachEntityWithOrphans(TEntity entity)
    {
        // Detach orphan entities first
        var parentId = GetEntityId(entity);
        if (_deletedChildrenByParent.TryGetValue(parentId, out var deletedChildren))
        {
            foreach (var deletedChild in deletedChildren)
            {
                var childEntry = _context.Entry(deletedChild);
                if (childEntry.State != EntityState.Detached)
                {
                    childEntry.State = EntityState.Detached;
                }
            }
        }

        // Detach the full entity graph (parent + children)
        DetachEntityGraph(entity);
    }

    internal void DetachAllEntities(List<TEntity> entities)
    {
        // First, detach all deleted children (they're no longer in navigation collections)
        foreach (var deletedChildren in _deletedChildrenByParent.Values)
        {
            foreach (var deletedChild in deletedChildren)
            {
                var entry = _context.Entry(deletedChild);
                if (entry.State != EntityState.Detached)
                {
                    entry.State = EntityState.Detached;
                }
            }
        }

        // Then detach the entity graphs (parent + current children)
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

    // ========== Orphan Detection Methods ==========

    internal void CaptureAllOriginalChildIds(List<TEntity> entities)
    {
        // Detect changes first to identify deleted children
        _context.ChangeTracker.DetectChanges();

        foreach (var entity in entities)
        {
            CaptureOriginalChildIdsFromChangeTracker(entity);
        }
    }

    private void CaptureOriginalChildIdsFromChangeTracker(TEntity entity)
    {
        var parentId = GetEntityId(entity);
        var childIds = new HashSet<int>();
        var deletedChildren = new List<object>();

        // Get current children (still in collection)
        var currentIds = GetChildIds(entity);
        foreach (var id in currentIds)
        {
            childIds.Add(id);
        }

        // Get deleted children from change tracker
        var entry = _context.Entry(entity);
        foreach (var navigation in entry.Navigations)
        {
            if (!navigation.Metadata.IsCollection)
            {
                continue;
            }

            AddDeletedChildrenFromChangeTracker(navigation, childIds, deletedChildren, parentId);
        }

        _originalChildIdsByParent[parentId] = childIds;
        _deletedChildrenByParent[parentId] = deletedChildren;
    }

    private void AddDeletedChildrenFromChangeTracker(
        Microsoft.EntityFrameworkCore.ChangeTracking.NavigationEntry navigation,
        HashSet<int> childIds,
        List<object> deletedChildren,
        int parentId)
    {
        var entityType = navigation.Metadata.TargetEntityType;
        var keyProperty = entityType.FindPrimaryKey()?.Properties.FirstOrDefault();

        if (keyProperty?.ClrType != typeof(int))
        {
            return;
        }

        // Get the foreign key property name that links to the parent
        var fkProperty = GetForeignKeyProperty(navigation);
        if (fkProperty == null)
        {
            return;
        }

        // Find deleted entities of this type that belong to this parent
        foreach (var trackedEntry in _context.ChangeTracker.Entries())
        {
            if (trackedEntry.Metadata != entityType || trackedEntry.State != EntityState.Deleted)
            {
                continue;
            }

            // Check if this deleted entity belongs to the current parent
            var fkValue = trackedEntry.Property(fkProperty.Name).CurrentValue;
            if (fkValue is not int childParentId || childParentId != parentId)
            {
                continue;
            }

            var keyValue = trackedEntry.Property(keyProperty.Name).CurrentValue;
            if (keyValue is int id)
            {
                childIds.Add(id);
                deletedChildren.Add(trackedEntry.Entity);
            }
        }
    }

    private static Microsoft.EntityFrameworkCore.Metadata.IProperty? GetForeignKeyProperty(
        Microsoft.EntityFrameworkCore.ChangeTracking.NavigationEntry navigation)
    {
        if (navigation.Metadata is Microsoft.EntityFrameworkCore.Metadata.INavigation navMetadata)
        {
            return navMetadata.ForeignKey.Properties.FirstOrDefault();
        }
        return null;
    }

    internal List<int> GetOrphanedChildIds(TEntity entity)
    {
        var parentId = GetEntityId(entity);
        var currentChildIds = GetChildIds(entity).ToHashSet();

        if (!_originalChildIdsByParent.TryGetValue(parentId, out var originalChildIds))
        {
            return [];
        }

        return originalChildIds.Where(id => !currentChildIds.Contains(id)).ToList();
    }

    internal void ValidateNoOrphanedChildren(TEntity entity, GraphBatchOptions options)
    {
        if (options.OrphanedChildBehavior != OrphanBehavior.Throw)
        {
            return;
        }

        var orphanedIds = GetOrphanedChildIds(entity);
        if (orphanedIds.Count == 0)
        {
            return;
        }

        var parentId = GetEntityId(entity);
        throw new InvalidOperationException(
            $"Entity {typeof(TEntity).Name} (Id={parentId}) has {orphanedIds.Count} orphaned child(ren) " +
            $"(IDs: {string.Join(", ", orphanedIds)}). " +
            $"Set GraphBatchOptions.OrphanedChildBehavior to Delete or Detach to allow this.");
    }

    internal void HandleOrphanedChildren(TEntity entity, GraphBatchOptions options)
    {
        if (options.OrphanedChildBehavior == OrphanBehavior.Detach)
        {
            return; // Leave orphans as-is
        }

        if (options.OrphanedChildBehavior == OrphanBehavior.Delete)
        {
            DeleteOrphanedChildren(entity);
        }
    }

    private void DeleteOrphanedChildren(TEntity entity)
    {
        var parentId = GetEntityId(entity);

        if (!_deletedChildrenByParent.TryGetValue(parentId, out var deletedChildren))
        {
            return;
        }

        // Re-attach the original deleted entities (which have correct Version values)
        foreach (var deletedChild in deletedChildren)
        {
            var entry = _context.Entry(deletedChild);
            if (entry.State == EntityState.Detached)
            {
                entry.State = EntityState.Deleted;
            }
        }
    }
}
