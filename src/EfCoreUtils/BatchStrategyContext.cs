using EfCoreUtils.Internal;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils;

internal class BatchStrategyContext<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly DbContext _context;
    private int _roundTripCounter;
    private readonly Dictionary<TKey, HashSet<TKey>> _originalChildIdsByParent = [];
    private readonly Dictionary<TKey, List<object>> _deletedChildrenByParent = [];

    // Multi-level orphan tracking (key includes type name for disambiguation)
    private readonly Dictionary<(string Type, TKey Id), HashSet<(string Type, TKey Id)>>
        _originalChildIdsByParentRecursive = [];
    private readonly Dictionary<(string Type, TKey Id), List<object>>
        _deletedChildrenByParentRecursive = [];

    private GraphHierarchyBuilder<TKey>? _graphBuilder;

    internal BatchStrategyContext(DbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _roundTripCounter = 0;
    }

    private GraphHierarchyBuilder<TKey> GraphBuilder =>
        _graphBuilder ??= new GraphHierarchyBuilder<TKey>(_context, GetEntityIdFromEntry);

    internal DbContext Context => _context;
    internal int RoundTripCounter => _roundTripCounter;
    internal void IncrementRoundTrip() => _roundTripCounter++;

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

    internal BatchFailure<TKey> CreateBatchFailure(TKey entityId, Exception exception)
    {
        var reason = exception switch
        {
            InvalidOperationException => FailureReason.ValidationError,
            DbUpdateConcurrencyException => FailureReason.ConcurrencyConflict,
            DbUpdateException => FailureReason.DatabaseConstraint,
            _ => FailureReason.UnknownError
        };

        return new BatchFailure<TKey>
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

        // Also detach all deleted children tracked by recursive methods
        foreach (var deletedChildren in _deletedChildrenByParentRecursive.Values)
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

    internal void DetachEntityGraph(TEntity entity)
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
                        // Create a snapshot to avoid collection modification during iteration
                        var items = collection.Cast<object>().ToList();
                        foreach (var item in items)
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
                $"BatchSaver<{typeof(TEntity).Name}, {typeof(TKey).Name}> only updates parent entities. " +
                $"To update entity graphs, use standard EF Core SaveChanges() or set " +
                $"BatchOptions.ValidateNavigationProperties = false to suppress this check.");
        }
    }

    internal void ValidateNoPopulatedNavigationProperties(TEntity entity)
    {
        var entry = _context.Entry(entity);
        var populatedNavigations = new List<string>();

        foreach (var navigation in entry.Navigations)
        {
            if (navigation.CurrentValue == null)
            {
                continue;
            }

            if (navigation.Metadata.IsCollection)
            {
                if (navigation.CurrentValue is System.Collections.IEnumerable collection)
                {
                    var hasItems = collection.Cast<object>().Any();
                    if (hasItems)
                    {
                        populatedNavigations.Add($"{navigation.Metadata.Name} (collection)");
                    }
                }
            }
            else
            {
                populatedNavigations.Add(navigation.Metadata.Name);
            }
        }

        if (populatedNavigations.Count != 0)
        {
            throw new InvalidOperationException(
                $"Entity {typeof(TEntity).Name} has populated navigation properties: " +
                $"{string.Join(", ", populatedNavigations)}. " +
                $"Use InsertGraphBatch to insert parent with children, or clear the navigations.");
        }
    }

    internal void AttachEntityGraphAsAdded(TEntity entity)
    {
        var entry = _context.Entry(entity);
        entry.State = EntityState.Added;

        foreach (var navigation in entry.Navigations)
        {
            if (navigation.CurrentValue == null || !navigation.Metadata.IsCollection)
            {
                continue;
            }

            AttachCollectionChildrenAsAdded(navigation);
        }
    }

    private void AttachCollectionChildrenAsAdded(
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
                itemEntry.State = EntityState.Added;
            }
        }
    }

    internal InsertBatchFailure CreateInsertBatchFailure(int entityIndex, Exception exception)
    {
        var reason = exception switch
        {
            InvalidOperationException => FailureReason.ValidationError,
            DbUpdateConcurrencyException => FailureReason.ConcurrencyConflict,
            DbUpdateException => FailureReason.DatabaseConstraint,
            _ => FailureReason.UnknownError
        };

        return new InsertBatchFailure
        {
            EntityIndex = entityIndex,
            ErrorMessage = exception.Message,
            Reason = reason,
            Exception = exception
        };
    }

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

    internal List<TKey> GetChildIds(TEntity entity)
    {
        var childIds = new List<TKey>();
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
        List<TKey> childIds)
    {
        if (navigation.CurrentValue is not System.Collections.IEnumerable collection)
        {
            return;
        }

        foreach (var item in collection)
        {
            var itemEntry = _context.Entry(item);
            var keyProperty = itemEntry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
            if (keyProperty?.ClrType == typeof(TKey))
            {
                var keyValue = itemEntry.Property(keyProperty.Name).CurrentValue;
                if (keyValue is TKey id)
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
        var childIds = new HashSet<TKey>();
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
        HashSet<TKey> childIds,
        List<object> deletedChildren,
        TKey parentId)
    {
        var entityType = navigation.Metadata.TargetEntityType;
        var keyProperty = entityType.FindPrimaryKey()?.Properties.FirstOrDefault();

        if (keyProperty?.ClrType != typeof(TKey))
        {
            return;
        }

        // Get the foreign key property name that links to the parent
        var fkProperty = NavigationPropertyHelper.GetForeignKeyProperty(navigation);
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
            if (fkValue is not TKey childParentId || !childParentId.Equals(parentId))
            {
                continue;
            }

            var keyValue = trackedEntry.Property(keyProperty.Name).CurrentValue;
            if (keyValue is TKey id)
            {
                childIds.Add(id);
                deletedChildren.Add(trackedEntry.Entity);
            }
        }
    }

    internal List<TKey> GetOrphanedChildIds(TEntity entity)
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

    // ========== Delete Operation Methods ==========

    internal void AttachEntityAsDeleted(TEntity entity)
    {
        _context.Entry(entity).State = EntityState.Deleted;
    }

    internal void AttachEntityGraphAsDeleted(TEntity entity)
    {
        var entry = _context.Entry(entity);

        // Mark children as Deleted FIRST (to respect FK constraints)
        foreach (var navigation in entry.Navigations)
        {
            if (navigation.CurrentValue == null || !navigation.Metadata.IsCollection)
            {
                continue;
            }

            MarkCollectionChildrenAsDeleted(navigation);
        }

        // Then mark parent as Deleted
        entry.State = EntityState.Deleted;
    }

    private void MarkCollectionChildrenAsDeleted(
        Microsoft.EntityFrameworkCore.ChangeTracking.NavigationEntry navigation)
    {
        if (navigation.CurrentValue is not System.Collections.IEnumerable collection)
        {
            return;
        }

        // Create a snapshot to avoid collection modification during iteration
        var items = collection.Cast<object>().ToList();
        foreach (var item in items)
        {
            var itemEntry = _context.Entry(item);
            itemEntry.State = EntityState.Deleted;
        }
    }

    internal void ValidateNoPopulatedNavigationPropertiesForDelete(TEntity entity)
    {
        var entry = _context.Entry(entity);
        var populatedNavigations = new List<string>();

        foreach (var navigation in entry.Navigations)
        {
            if (navigation.CurrentValue == null)
            {
                continue;
            }

            if (navigation.Metadata.IsCollection)
            {
                if (navigation.CurrentValue is System.Collections.IEnumerable collection)
                {
                    var hasItems = collection.Cast<object>().Any();
                    if (hasItems)
                    {
                        populatedNavigations.Add($"{navigation.Metadata.Name} (collection)");
                    }
                }
            }
            else
            {
                populatedNavigations.Add(navigation.Metadata.Name);
            }
        }

        if (populatedNavigations.Count != 0)
        {
            var entityId = GetEntityId(entity);
            throw new InvalidOperationException(
                $"Entity {typeof(TEntity).Name} (Id={entityId}) has populated navigation properties: " +
                $"{string.Join(", ", populatedNavigations)}. " +
                $"Use DeleteGraphBatch to delete parent with children, or remove Include().");
        }
    }

    internal void ValidateCascadeBehavior(TEntity entity, DeleteGraphBatchOptions options)
    {
        if (options.CascadeBehavior != DeleteCascadeBehavior.Throw)
        {
            return;
        }

        var entry = _context.Entry(entity);
        var entityId = GetEntityId(entity);

        foreach (var navigation in entry.Navigations)
        {
            if (navigation.CurrentValue == null || !navigation.Metadata.IsCollection)
            {
                continue;
            }

            if (navigation.CurrentValue is System.Collections.IEnumerable collection)
            {
                var childCount = collection.Cast<object>().Count();
                if (childCount > 0)
                {
                    throw new InvalidOperationException(
                        $"Entity {typeof(TEntity).Name} (Id={entityId}) has {childCount} child(ren) in '{navigation.Metadata.Name}'. " +
                        $"Set DeleteGraphBatchOptions.CascadeBehavior to Cascade or ParentOnly to proceed.");
                }
            }
        }
    }

    private TKey GetEntityIdFromEntry(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
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

    internal (GraphNode<TKey> Node, GraphTraversalResult<TKey> Stats) BuildGraphHierarchy(
        TEntity entity, int maxDepth) => GraphBuilder.Build(entity, maxDepth);

    // ========== Recursive Attachment Methods ==========

    internal void AttachEntityGraphAsAddedRecursive(TEntity entity, int maxDepth)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        AttachAsAddedRecursive(entity, 0, maxDepth, visited);
    }

    private void AttachAsAddedRecursive(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited)
    {
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);
        entry.State = EntityState.Added;

        if (currentDepth >= maxDepth)
        {
            return;
        }

        AttachChildrenRecursive(entry, currentDepth, maxDepth, visited, EntityState.Added);
    }

    internal void AttachEntityGraphAsModifiedRecursive(TEntity entity, int maxDepth)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        AttachAsModifiedRecursive(entity, 0, maxDepth, visited);
    }

    private void AttachAsModifiedRecursive(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited)
    {
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);
        if (entry.State == EntityState.Detached)
        {
            entry.State = EntityState.Modified;
        }

        if (currentDepth >= maxDepth)
        {
            return;
        }

        AttachChildrenRecursive(entry, currentDepth, maxDepth, visited, EntityState.Modified);
    }

    private void AttachChildrenRecursive(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
        int currentDepth, int maxDepth, HashSet<object> visited, EntityState targetState)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                if (targetState == EntityState.Added)
                {
                    AttachAsAddedRecursive(item, currentDepth + 1, maxDepth, visited);
                }
                else
                {
                    AttachAsModifiedRecursive(item, currentDepth + 1, maxDepth, visited);
                }
            }
        }
    }

    // CRITICAL: Delete uses depth-first order - children before parent for FK constraints
    internal void AttachEntityGraphAsDeletedRecursive(TEntity entity, int maxDepth)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        AttachAsDeletedRecursive(entity, 0, maxDepth, visited);
    }

    private void AttachAsDeletedRecursive(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited)
    {
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);

        // Depth-first: process children BEFORE marking parent as Deleted
        if (currentDepth < maxDepth)
        {
            DeleteChildrenRecursive(entry, currentDepth, maxDepth, visited);
        }

        // Then mark this entity as Deleted
        entry.State = EntityState.Deleted;
    }

    private void DeleteChildrenRecursive(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
        int currentDepth, int maxDepth, HashSet<object> visited)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                AttachAsDeletedRecursive(item, currentDepth + 1, maxDepth, visited);
            }
        }
    }

    // ========== Recursive Detachment Methods ==========

    internal void DetachEntityGraphRecursive(TEntity entity, int maxDepth)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        DetachRecursive(entity, 0, maxDepth, visited);
    }

    private void DetachRecursive(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited)
    {
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);

        // Detach children first
        if (currentDepth < maxDepth)
        {
            DetachChildrenRecursive(entry, currentDepth, maxDepth, visited);
        }

        // Then detach this entity
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Detached;
        }
    }

    private void DetachChildrenRecursive(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
        int currentDepth, int maxDepth, HashSet<object> visited)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                DetachRecursive(item, currentDepth + 1, maxDepth, visited);
            }
        }
    }

    // ========== Recursive Orphan Detection Methods ==========

    internal void CaptureAllOriginalChildIdsRecursive(List<TEntity> entities, int maxDepth)
    {
        _context.ChangeTracker.DetectChanges();

        foreach (var entity in entities)
        {
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            CaptureChildIdsRecursive(entity, 0, maxDepth, visited);
        }
    }

    private void CaptureChildIdsRecursive(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited)
    {
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);
        var parentKey = CreateEntityKey(entry);

        CaptureDirectChildIds(entry, parentKey);
        CaptureDeletedChildrenFromTracker(entry, parentKey);

        if (currentDepth >= maxDepth)
        {
            return;
        }

        // Recurse into children
        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                CaptureChildIdsRecursive(item, currentDepth + 1, maxDepth, visited);
            }
        }
    }

    private (string Type, TKey Id) CreateEntityKey(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        return (entry.Metadata.ClrType.Name, GetEntityIdFromEntry(entry));
    }

    private void CaptureDirectChildIds(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
        (string Type, TKey Id) parentKey)
    {
        var childIds = new HashSet<(string Type, TKey Id)>();

        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                var childEntry = _context.Entry(item);
                var childKey = CreateEntityKey(childEntry);
                childIds.Add(childKey);
            }
        }

        if (!_originalChildIdsByParentRecursive.TryGetValue(parentKey, out var existing))
        {
            _originalChildIdsByParentRecursive[parentKey] = childIds;
        }
        else
        {
            foreach (var id in childIds)
            {
                existing.Add(id);
            }
        }
    }

    private void CaptureDeletedChildrenFromTracker(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
        (string Type, TKey Id) parentKey)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!navigation.Metadata.IsCollection)
            {
                continue;
            }

            CaptureDeletedChildrenForNavigation(navigation, parentKey);
        }
    }

    private void CaptureDeletedChildrenForNavigation(
        Microsoft.EntityFrameworkCore.ChangeTracking.NavigationEntry navigation,
        (string Type, TKey Id) parentKey)
    {
        var entityType = navigation.Metadata.TargetEntityType;
        var keyProperty = entityType.FindPrimaryKey()?.Properties.FirstOrDefault();

        if (keyProperty?.ClrType != typeof(TKey))
        {
            return;
        }

        var fkProperty = NavigationPropertyHelper.GetForeignKeyProperty(navigation);
        if (fkProperty == null)
        {
            return;
        }

        foreach (var trackedEntry in _context.ChangeTracker.Entries())
        {
            if (trackedEntry.Metadata != entityType || trackedEntry.State != EntityState.Deleted)
            {
                continue;
            }

            AddDeletedChildIfBelongsToParent(trackedEntry, fkProperty, parentKey, keyProperty);
        }
    }

    private void AddDeletedChildIfBelongsToParent(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry trackedEntry,
        Microsoft.EntityFrameworkCore.Metadata.IProperty fkProperty,
        (string Type, TKey Id) parentKey,
        Microsoft.EntityFrameworkCore.Metadata.IProperty keyProperty)
    {
        var fkValue = trackedEntry.Property(fkProperty.Name).CurrentValue;
        if (fkValue is not TKey childParentId || !childParentId.Equals(parentKey.Id))
        {
            return;
        }

        var keyValue = trackedEntry.Property(keyProperty.Name).CurrentValue;
        if (keyValue is not TKey childId)
        {
            return;
        }

        var childKey = (trackedEntry.Metadata.ClrType.Name, childId);

        // Add to original IDs
        if (!_originalChildIdsByParentRecursive.TryGetValue(parentKey, out var originalIds))
        {
            originalIds = [];
            _originalChildIdsByParentRecursive[parentKey] = originalIds;
        }
        originalIds.Add(childKey);

        // Add to deleted children list
        if (!_deletedChildrenByParentRecursive.TryGetValue(parentKey, out var deletedList))
        {
            deletedList = [];
            _deletedChildrenByParentRecursive[parentKey] = deletedList;
        }
        deletedList.Add(trackedEntry.Entity);
    }

    internal List<(string EntityType, TKey EntityId, int Depth)> GetOrphanedChildIdsRecursive(
        TEntity entity, int maxDepth)
    {
        var orphans = new List<(string EntityType, TKey EntityId, int Depth)>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        CollectOrphansRecursive(entity, 0, maxDepth, visited, orphans);
        return orphans;
    }

    private void CollectOrphansRecursive(
        object entity, int currentDepth, int maxDepth,
        HashSet<object> visited, List<(string, TKey, int)> orphans)
    {
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);
        var parentKey = CreateEntityKey(entry);

        CollectOrphansForEntity(entry, parentKey, currentDepth, orphans);

        if (currentDepth >= maxDepth)
        {
            return;
        }

        // Recurse into children
        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                CollectOrphansRecursive(item, currentDepth + 1, maxDepth, visited, orphans);
            }
        }
    }

    private void CollectOrphansForEntity(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
        (string Type, TKey Id) parentKey, int depth,
        List<(string, TKey, int)> orphans)
    {
        if (!_originalChildIdsByParentRecursive.TryGetValue(parentKey, out var originalIds))
        {
            return;
        }

        var currentIds = GetCurrentChildKeysForEntity(entry);

        foreach (var originalId in originalIds)
        {
            if (!currentIds.Contains(originalId))
            {
                orphans.Add((originalId.Type, originalId.Id, depth + 1));
            }
        }
    }

    private HashSet<(string Type, TKey Id)> GetCurrentChildKeysForEntity(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        var currentIds = new HashSet<(string Type, TKey Id)>();

        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                var childEntry = _context.Entry(item);
                currentIds.Add(CreateEntityKey(childEntry));
            }
        }

        return currentIds;
    }

    internal void ValidateNoOrphanedChildrenRecursive(
        TEntity entity, int maxDepth, GraphBatchOptions options)
    {
        if (options.OrphanedChildBehavior != OrphanBehavior.Throw)
        {
            return;
        }

        var orphanedIds = GetOrphanedChildIdsRecursive(entity, maxDepth);
        if (orphanedIds.Count == 0)
        {
            return;
        }

        var entityId = GetEntityId(entity);
        var summary = string.Join(", ", orphanedIds.Select(o => $"{o.EntityType}:{o.EntityId}@depth{o.Depth}"));
        throw new InvalidOperationException(
            $"Entity {typeof(TEntity).Name} (Id={entityId}) has {orphanedIds.Count} orphaned descendant(s): " +
            $"[{summary}]. " +
            $"Set GraphBatchOptions.OrphanedChildBehavior to Delete or Detach to allow this.");
    }

    internal void HandleOrphanedChildrenRecursive(
        TEntity entity, int maxDepth, OrphanBehavior behavior)
    {
        if (behavior == OrphanBehavior.Detach)
        {
            // Explicitly detach orphans from change tracker so SaveChanges won't delete them
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            DetachOrphansAtAllLevels(entity, 0, maxDepth, visited);
            return;
        }

        if (behavior == OrphanBehavior.Delete)
        {
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            DeleteOrphansAtAllLevels(entity, 0, maxDepth, visited);
        }
    }

    private void DetachOrphansAtAllLevels(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited)
    {
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);
        var parentKey = CreateEntityKey(entry);

        DetachDeletedChildrenFromTracker(parentKey);

        if (currentDepth >= maxDepth)
        {
            return;
        }

        // Recurse into children
        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                DetachOrphansAtAllLevels(item, currentDepth + 1, maxDepth, visited);
            }
        }
    }

    private void DetachDeletedChildrenFromTracker((string Type, TKey Id) parentKey)
    {
        if (!_deletedChildrenByParentRecursive.TryGetValue(parentKey, out var deletedChildren))
        {
            return;
        }

        foreach (var deletedChild in deletedChildren)
        {
            var entry = _context.Entry(deletedChild);
            if (entry.State != EntityState.Detached)
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private void DeleteOrphansAtAllLevels(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited)
    {
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);
        var parentKey = CreateEntityKey(entry);

        ReattachDeletedChildrenAsDeleted(parentKey);

        if (currentDepth >= maxDepth)
        {
            return;
        }

        // Recurse into children
        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                DeleteOrphansAtAllLevels(item, currentDepth + 1, maxDepth, visited);
            }
        }
    }

    private void ReattachDeletedChildrenAsDeleted((string Type, TKey Id) parentKey)
    {
        if (!_deletedChildrenByParentRecursive.TryGetValue(parentKey, out var deletedChildren))
        {
            return;
        }

        foreach (var deletedChild in deletedChildren)
        {
            var entry = _context.Entry(deletedChild);
            if (entry.State == EntityState.Detached)
            {
                entry.State = EntityState.Deleted;
            }
        }
    }

    // ========== Recursive Detach with Orphans Methods ==========

    internal void DetachEntityWithOrphansRecursive(TEntity entity, int maxDepth)
    {
        // First detach all deleted children tracked at any level
        DetachAllDeletedChildrenRecursive();

        // Then detach the full entity graph
        DetachEntityGraphRecursive(entity, maxDepth);
    }

    private void DetachAllDeletedChildrenRecursive()
    {
        foreach (var deletedChildren in _deletedChildrenByParentRecursive.Values)
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
    }

    // ========== Recursive Cascade Validation Methods ==========

    internal void ValidateCascadeBehaviorRecursive(
        TEntity entity, int maxDepth, DeleteGraphBatchOptions options)
    {
        if (options.CascadeBehavior != DeleteCascadeBehavior.Throw)
        {
            return;
        }

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        ValidateCascadeRecursive(entity, 0, maxDepth, visited);
    }

    private void ValidateCascadeRecursive(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited)
    {
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);
        ValidateEntityHasNoChildren(entry, currentDepth);

        if (currentDepth >= maxDepth)
        {
            return;
        }

        // Recurse into children
        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                ValidateCascadeRecursive(item, currentDepth + 1, maxDepth, visited);
            }
        }
    }

    private void ValidateEntityHasNoChildren(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, int depth)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (navigation.CurrentValue == null || !navigation.Metadata.IsCollection)
            {
                continue;
            }

            if (navigation.CurrentValue is System.Collections.IEnumerable collection)
            {
                var childCount = collection.Cast<object>().Count();
                if (childCount > 0)
                {
                    var entityId = GetEntityIdFromEntry(entry);
                    throw new InvalidOperationException(
                        $"Entity {entry.Metadata.ClrType.Name} (Id={entityId}) at depth {depth} has " +
                        $"{childCount} child(ren) in '{navigation.Metadata.Name}'. " +
                        $"Set DeleteGraphBatchOptions.CascadeBehavior to Cascade or ParentOnly to proceed.");
                }
            }
        }
    }
}
