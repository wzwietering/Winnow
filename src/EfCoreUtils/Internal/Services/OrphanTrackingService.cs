using EfCoreUtils.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EfCoreUtils.Internal.Services;

internal class OrphanTrackingService<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly DbContext _context;
    private readonly EntityKeyService<TEntity, TKey> _keyService;

    // Single-level orphan tracking
    private readonly Dictionary<TKey, HashSet<TKey>> _originalChildIdsByParent = [];
    private readonly Dictionary<TKey, List<object>> _deletedChildrenByParent = [];

    // Multi-level orphan tracking (key includes type name for disambiguation)
    private readonly Dictionary<(string Type, TKey Id), HashSet<(string Type, TKey Id)>>
        _originalChildIdsByParentRecursive = [];
    private readonly Dictionary<(string Type, TKey Id), List<object>>
        _deletedChildrenByParentRecursive = [];

    // Performance optimization: index of deleted entries by type
    private Dictionary<IEntityType, List<EntityEntry>>? _deletedEntriesIndex;

    internal OrphanTrackingService(DbContext context, EntityKeyService<TEntity, TKey> keyService)
    {
        _context = context;
        _keyService = keyService;
    }

    internal Dictionary<TKey, List<object>> DeletedChildrenByParent => _deletedChildrenByParent;

    internal Dictionary<(string Type, TKey Id), List<object>> DeletedChildrenByParentRecursive =>
        _deletedChildrenByParentRecursive;

    // ========== Performance Optimization ==========

    private Dictionary<IEntityType, List<EntityEntry>> GetOrBuildDeletedIndex() => _deletedEntriesIndex ??= _context.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Deleted)
            .GroupBy(e => e.Metadata)
            .ToDictionary(g => g.Key, g => g.ToList());

    private void InvalidateDeletedIndex() => _deletedEntriesIndex = null;

    // ========== Single-Level Orphan Detection ==========

    internal void CaptureAllOriginalChildIds(List<TEntity> entities)
    {
        _context.ChangeTracker.DetectChanges();
        InvalidateDeletedIndex();

        foreach (var entity in entities)
        {
            CaptureOriginalChildIdsFromChangeTracker(entity);
        }
    }

    private void CaptureOriginalChildIdsFromChangeTracker(TEntity entity)
    {
        var parentId = _keyService.GetEntityId(entity);
        var (childIds, deletedChildren) = CollectChildrenAndDeleted(entity, parentId);

        _originalChildIdsByParent[parentId] = childIds;
        _deletedChildrenByParent[parentId] = deletedChildren;
    }

    private (HashSet<TKey> childIds, List<object> deleted) CollectChildrenAndDeleted(TEntity entity, TKey parentId)
    {
        var childIds = GetChildIds(entity).ToHashSet();
        var deletedChildren = new List<object>();

        var entry = _context.Entry(entity);
        foreach (var navigation in entry.Navigations.Where(n => n.Metadata.IsCollection))
        {
            AddDeletedChildrenFromChangeTracker(navigation, childIds, deletedChildren, parentId);
        }

        return (childIds, deletedChildren);
    }

    private void AddDeletedChildrenFromChangeTracker(
        NavigationEntry navigation,
        HashSet<TKey> childIds,
        List<object> deletedChildren,
        TKey parentId)
    {
        var entityType = navigation.Metadata.TargetEntityType;
        var keyProperties = entityType.FindPrimaryKey()?.Properties;

        if (!CompositeKeyHelper.IsCompatibleKeyType<TKey>(keyProperties))
        {
            return;
        }

        var fkProperties = NavigationPropertyHelper.GetForeignKeyProperties(navigation);
        if (fkProperties == null || fkProperties.Count == 0)
        {
            return;
        }

        AddMatchingDeletedChildren(entityType, fkProperties, keyProperties!, parentId, childIds, deletedChildren);
    }

    private void AddMatchingDeletedChildren(
        IEntityType entityType,
        IReadOnlyList<IProperty> fkProperties,
        IReadOnlyList<IProperty> keyProperties,
        TKey parentId,
        HashSet<TKey> childIds,
        List<object> deletedChildren)
    {
        var deletedIndex = GetOrBuildDeletedIndex();
        if (!deletedIndex.TryGetValue(entityType, out var deletedEntries))
        {
            return;
        }

        foreach (var trackedEntry in deletedEntries)
        {
            TryAddDeletedChild(trackedEntry, fkProperties, keyProperties, parentId, childIds, deletedChildren);
        }
    }

    private static void TryAddDeletedChild(
        EntityEntry trackedEntry,
        IReadOnlyList<IProperty> fkProperties,
        IReadOnlyList<IProperty> keyProperties,
        TKey parentId,
        HashSet<TKey> childIds,
        List<object> deletedChildren)
    {
        if (!CompositeKeyHelper.ForeignKeyMatchesParent(trackedEntry, fkProperties, parentId))
        {
            return;
        }

        var keyValue = CompositeKeyHelper.ExtractEntityId(trackedEntry, keyProperties);
        if (keyValue is TKey id)
        {
            childIds.Add(id);
            deletedChildren.Add(trackedEntry.Entity);
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

    private void AddChildIdsFromCollection(NavigationEntry navigation, List<TKey> childIds)
    {
        if (navigation.CurrentValue is not System.Collections.IEnumerable collection)
        {
            return;
        }

        foreach (var item in collection)
        {
            var itemEntry = _context.Entry(item);
            var keyProperties = itemEntry.Metadata.FindPrimaryKey()?.Properties;
            if (CompositeKeyHelper.IsCompatibleKeyType<TKey>(keyProperties))
            {
                var keyValue = CompositeKeyHelper.ExtractEntityId(itemEntry, keyProperties!);
                if (keyValue is TKey id)
                {
                    childIds.Add(id);
                }
            }
        }
    }

    internal List<TKey> GetOrphanedChildIds(TEntity entity)
    {
        var parentId = _keyService.GetEntityId(entity);
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

        var parentId = _keyService.GetEntityId(entity);
        throw new InvalidOperationException(
            $"Entity {typeof(TEntity).Name} (Id={parentId}) has {orphanedIds.Count} orphaned child(ren) " +
            $"(IDs: {string.Join(", ", orphanedIds)}). " +
            $"Set GraphBatchOptions.OrphanedChildBehavior to Delete or Detach to allow this.");
    }

    internal void HandleOrphanedChildren(TEntity entity, GraphBatchOptions options)
    {
        if (options.OrphanedChildBehavior == OrphanBehavior.Detach)
        {
            return;
        }

        if (options.OrphanedChildBehavior == OrphanBehavior.Delete)
        {
            DeleteOrphanedChildren(entity);
        }
    }

    private void DeleteOrphanedChildren(TEntity entity)
    {
        var parentId = _keyService.GetEntityId(entity);

        if (!_deletedChildrenByParent.TryGetValue(parentId, out var deletedChildren))
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

    // ========== Multi-Level Orphan Detection ==========

    internal void CaptureAllOriginalChildIdsRecursive(List<TEntity> entities, int maxDepth)
    {
        _context.ChangeTracker.DetectChanges();
        InvalidateDeletedIndex();
        var clampedDepth = ClampDepth(maxDepth);

        foreach (var entity in entities)
        {
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            CaptureChildIdsRecursive(entity, 0, clampedDepth, visited);
        }
    }

    private void CaptureChildIdsRecursive(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited)
    {
        if (!visited.Add(entity)) return;

        var entry = _context.Entry(entity);
        var parentKey = _keyService.CreateEntityKey(entry);

        CaptureDirectChildIds(entry, parentKey);
        CaptureDeletedChildrenFromTracker(entry, parentKey);

        if (currentDepth < maxDepth)
        {
            TraverseChildNavigations(entry, currentDepth, maxDepth, visited);
        }
    }

    private void TraverseChildNavigations(
        EntityEntry entry, int currentDepth, int maxDepth, HashSet<object> visited)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation)) continue;

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                CaptureChildIdsRecursive(item, currentDepth + 1, maxDepth, visited);
            }
        }
    }

    private void CaptureDirectChildIds(EntityEntry entry, (string Type, TKey Id) parentKey)
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
                var childKey = _keyService.CreateEntityKey(childEntry);
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

    private void CaptureDeletedChildrenFromTracker(EntityEntry entry, (string Type, TKey Id) parentKey)
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
        NavigationEntry navigation, (string Type, TKey Id) parentKey)
    {
        var entityType = navigation.Metadata.TargetEntityType;
        var keyProperties = entityType.FindPrimaryKey()?.Properties;

        if (!CompositeKeyHelper.IsCompatibleKeyType<TKey>(keyProperties))
        {
            return;
        }

        var fkProperties = NavigationPropertyHelper.GetForeignKeyProperties(navigation);
        if (fkProperties == null || fkProperties.Count == 0)
        {
            return;
        }

        ProcessDeletedEntriesForNavigation(entityType, fkProperties, keyProperties!, parentKey);
    }

    private void ProcessDeletedEntriesForNavigation(
        IEntityType entityType,
        IReadOnlyList<IProperty> fkProperties,
        IReadOnlyList<IProperty> keyProperties,
        (string Type, TKey Id) parentKey)
    {
        var deletedIndex = GetOrBuildDeletedIndex();
        if (!deletedIndex.TryGetValue(entityType, out var deletedEntries))
        {
            return;
        }

        foreach (var trackedEntry in deletedEntries)
        {
            AddDeletedChildIfBelongsToParent(trackedEntry, fkProperties, parentKey, keyProperties);
        }
    }

    private void AddDeletedChildIfBelongsToParent(
        EntityEntry trackedEntry, IReadOnlyList<IProperty> fkProperties,
        (string Type, TKey Id) parentKey, IReadOnlyList<IProperty> keyProperties)
    {
        if (!CompositeKeyHelper.ForeignKeyMatchesParent(trackedEntry, fkProperties, parentKey.Id))
        {
            return;
        }

        var keyValue = CompositeKeyHelper.ExtractEntityId(trackedEntry, keyProperties);
        if (keyValue is not TKey childId)
        {
            return;
        }

        AddChildToTracking(trackedEntry, parentKey, childId);
    }

    private void AddChildToTracking(
        EntityEntry trackedEntry, (string Type, TKey Id) parentKey, TKey childId)
    {
        var childKey = (trackedEntry.Metadata.ClrType.Name, childId);

        if (!_originalChildIdsByParentRecursive.TryGetValue(parentKey, out var originalIds))
        {
            originalIds = [];
            _originalChildIdsByParentRecursive[parentKey] = originalIds;
        }
        originalIds.Add(childKey);

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
        CollectOrphansRecursive(entity, 0, ClampDepth(maxDepth), visited, orphans);
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
        var parentKey = _keyService.CreateEntityKey(entry);

        CollectOrphansForEntity(entry, parentKey, currentDepth, orphans);

        if (currentDepth >= maxDepth)
        {
            return;
        }

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
        EntityEntry entry, (string Type, TKey Id) parentKey, int depth,
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

    private HashSet<(string Type, TKey Id)> GetCurrentChildKeysForEntity(EntityEntry entry)
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
                currentIds.Add(_keyService.CreateEntityKey(childEntry));
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

        var entityId = _keyService.GetEntityId(entity);
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
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            DetachOrphansAtAllLevels(entity, 0, ClampDepth(maxDepth), visited);
            return;
        }

        if (behavior == OrphanBehavior.Delete)
        {
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            DeleteOrphansAtAllLevels(entity, 0, ClampDepth(maxDepth), visited);
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
        var parentKey = _keyService.CreateEntityKey(entry);

        DetachDeletedChildrenFromTracker(parentKey);

        if (currentDepth >= maxDepth)
        {
            return;
        }

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
        var parentKey = _keyService.CreateEntityKey(entry);

        ReattachDeletedChildrenAsDeleted(parentKey);

        if (currentDepth >= maxDepth)
        {
            return;
        }

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

    internal void DetachEntityWithOrphans(TEntity entity, EntityDetachmentService<TEntity, TKey> detachmentService)
    {
        var parentId = _keyService.GetEntityId(entity);
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

        detachmentService.DetachEntityGraph(entity);
    }

    internal void DetachEntityWithOrphansRecursive(
        TEntity entity, int maxDepth, EntityDetachmentService<TEntity, TKey> detachmentService)
    {
        DetachAllDeletedChildrenRecursive();
        detachmentService.DetachEntityGraphRecursive(entity, maxDepth);
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

    private static int ClampDepth(int maxDepth) => DepthConstants.ClampDepth(maxDepth);
}
