using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Winnow.Internal.Services;

internal class RecursiveOrphanTracker<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly DbContext _context;
    private readonly EntityKeyService<TEntity, TKey> _keyService;
    private readonly Func<Dictionary<IEntityType, List<EntityEntry>>> _getDeletedIndex;
    private readonly Action _invalidateDeletedIndex;

    private readonly Dictionary<(string Type, TKey Id), HashSet<(string Type, TKey Id)>>
        _originalChildIdsByParentRecursive = [];
    private readonly Dictionary<(string Type, TKey Id), List<object>>
        _deletedChildrenByParentRecursive = [];

    internal RecursiveOrphanTracker(
        DbContext context,
        EntityKeyService<TEntity, TKey> keyService,
        Func<Dictionary<IEntityType, List<EntityEntry>>> getDeletedIndex,
        Action invalidateDeletedIndex)
    {
        _context = context;
        _keyService = keyService;
        _getDeletedIndex = getDeletedIndex;
        _invalidateDeletedIndex = invalidateDeletedIndex;
    }

    internal Dictionary<(string Type, TKey Id), List<object>> DeletedChildrenByParentRecursive =>
        _deletedChildrenByParentRecursive;

    internal void CaptureAllOriginalChildIdsRecursive(List<TEntity> entities, TraversalContext tc)
    {
        _context.ChangeTracker.DetectChanges();
        _invalidateDeletedIndex();
        var clampedDepth = DepthConstants.ClampDepth(tc.MaxDepth);

        foreach (var entity in entities)
        {
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            CaptureChildIdsRecursive(entity, 0, clampedDepth, visited, tc.NavigationFilter);
        }
    }

    private void CaptureChildIdsRecursive(
        object entity, int currentDepth, int maxDepth,
        HashSet<object> visited, NavigationFilter? filter)
    {
        if (!visited.Add(entity)) return;

        var entry = _context.Entry(entity);
        var parentKey = _keyService.CreateEntityKey(entry);

        CaptureDirectChildIds(entry, parentKey, filter);
        CaptureDeletedChildrenFromTracker(entry, parentKey, filter);

        if (currentDepth < maxDepth)
        {
            TraverseChildNavigations(entry, currentDepth, maxDepth, visited, filter);
        }
    }

    private void TraverseChildNavigations(
        EntityEntry entry, int currentDepth, int maxDepth,
        HashSet<object> visited, NavigationFilter? filter)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!TraversalHelper.ShouldTraverseCollection(navigation, filter, skipManyToMany: false))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                CaptureChildIdsRecursive(item, currentDepth + 1, maxDepth, visited, filter);
            }
        }
    }

    private void CaptureDirectChildIds(
        EntityEntry entry, (string Type, TKey Id) parentKey, NavigationFilter? filter)
    {
        var childIds = new HashSet<(string Type, TKey Id)>();

        foreach (var navigation in entry.Navigations)
        {
            if (!TraversalHelper.ShouldTraverseCollection(navigation, filter, skipManyToMany: false))
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

    private void CaptureDeletedChildrenFromTracker(
        EntityEntry entry, (string Type, TKey Id) parentKey, NavigationFilter? filter)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!navigation.Metadata.IsCollection)
            {
                continue;
            }

            if (filter != null)
            {
                var entityType = navigation.EntityEntry.Metadata.ClrType;
                if (!filter.ShouldTraverse(entityType, navigation.Metadata.Name))
                {
                    continue;
                }
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
        var deletedIndex = _getDeletedIndex();
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
        TEntity entity, TraversalContext tc)
    {
        var orphans = new List<(string EntityType, TKey EntityId, int Depth)>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        CollectOrphansRecursive(entity, 0, DepthConstants.ClampDepth(tc.MaxDepth), visited, orphans, tc.NavigationFilter);
        return orphans;
    }

    private void CollectOrphansRecursive(
        object entity, int currentDepth, int maxDepth,
        HashSet<object> visited, List<(string, TKey, int)> orphans,
        NavigationFilter? filter)
    {
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);
        var parentKey = _keyService.CreateEntityKey(entry);

        CollectOrphansForEntity(entry, parentKey, currentDepth, orphans, filter);

        if (currentDepth >= maxDepth)
        {
            return;
        }

        foreach (var navigation in entry.Navigations)
        {
            if (!TraversalHelper.ShouldTraverseCollection(navigation, filter, skipManyToMany: false))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                CollectOrphansRecursive(item, currentDepth + 1, maxDepth, visited, orphans, filter);
            }
        }
    }

    private void CollectOrphansForEntity(
        EntityEntry entry, (string Type, TKey Id) parentKey, int depth,
        List<(string, TKey, int)> orphans, NavigationFilter? filter)
    {
        if (!_originalChildIdsByParentRecursive.TryGetValue(parentKey, out var originalIds))
        {
            return;
        }

        var currentIds = GetCurrentChildKeysForEntity(entry, filter);

        foreach (var originalId in originalIds)
        {
            if (!currentIds.Contains(originalId))
            {
                orphans.Add((originalId.Type, originalId.Id, depth + 1));
            }
        }
    }

    private HashSet<(string Type, TKey Id)> GetCurrentChildKeysForEntity(
        EntityEntry entry, NavigationFilter? filter)
    {
        var currentIds = new HashSet<(string Type, TKey Id)>();

        foreach (var navigation in entry.Navigations)
        {
            if (!TraversalHelper.ShouldTraverseCollection(navigation, filter, skipManyToMany: false))
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
        TEntity entity, TraversalContext tc, GraphOptions options)
    {
        if (options.OrphanedChildBehavior != OrphanBehavior.Throw)
        {
            return;
        }

        var orphanedIds = GetOrphanedChildIdsRecursive(entity, tc);
        if (orphanedIds.Count == 0)
        {
            return;
        }

        var entityId = _keyService.GetEntityId(entity);
        var summary = string.Join(", ", orphanedIds.Select(o => $"{o.EntityType}:{o.EntityId}@depth{o.Depth}"));
        throw new InvalidOperationException(
            $"Entity {typeof(TEntity).Name} (Id={entityId}) has {orphanedIds.Count} orphaned descendant(s): " +
            $"[{summary}]. " +
            $"Set GraphOptions.OrphanedChildBehavior to Delete or Detach to allow this.");
    }

    internal void HandleOrphanedChildrenRecursive(
        TEntity entity, TraversalContext tc, OrphanBehavior behavior)
    {
        if (behavior == OrphanBehavior.Detach)
        {
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            DetachOrphansAtAllLevels(entity, 0, DepthConstants.ClampDepth(tc.MaxDepth), visited, tc.NavigationFilter);
            return;
        }

        if (behavior == OrphanBehavior.Delete)
        {
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            DeleteOrphansAtAllLevels(entity, 0, DepthConstants.ClampDepth(tc.MaxDepth), visited, tc.NavigationFilter);
        }
    }

    private void DetachOrphansAtAllLevels(
        object entity, int currentDepth, int maxDepth,
        HashSet<object> visited, NavigationFilter? filter)
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
            if (!TraversalHelper.ShouldTraverseCollection(navigation, filter, skipManyToMany: false))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                DetachOrphansAtAllLevels(item, currentDepth + 1, maxDepth, visited, filter);
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
        object entity, int currentDepth, int maxDepth,
        HashSet<object> visited, NavigationFilter? filter)
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
            if (!TraversalHelper.ShouldTraverseCollection(navigation, filter, skipManyToMany: false))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                DeleteOrphansAtAllLevels(item, currentDepth + 1, maxDepth, visited, filter);
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

    internal void DetachEntityWithOrphansRecursive(
        TEntity entity, TraversalContext tc,
        EntityDetachmentService<TEntity, TKey> detachmentService)
    {
        DetachAllDeletedChildrenRecursive();
        detachmentService.DetachEntityGraphRecursive(entity, tc);
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
}
