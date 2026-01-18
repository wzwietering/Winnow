using EfCoreUtils.MixedKey;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EfCoreUtils.Internal.Services.MixedKey;

/// <summary>
/// Tracks orphaned entities in graphs with mixed key types.
/// </summary>
internal class MixedKeyOrphanTrackingService<TEntity>
    where TEntity : class
{
    private const int AbsoluteMaxDepth = 100;

    private readonly DbContext _context;
    private readonly MixedKeyEntityKeyService _keyService;

    private readonly Dictionary<(string Type, object Id), HashSet<(string Type, object Id)>>
        _originalChildIdsByParentRecursive;
    private readonly Dictionary<(string Type, object Id), List<object>>
        _deletedChildrenByParentRecursive;

    private Dictionary<IEntityType, List<EntityEntry>>? _deletedEntriesIndex;

    internal MixedKeyOrphanTrackingService(DbContext context)
    {
        _context = context;
        _keyService = new MixedKeyEntityKeyService(context);
        _originalChildIdsByParentRecursive = new(MixedKeyComparer.Instance);
        _deletedChildrenByParentRecursive = new(MixedKeyComparer.Instance);
    }

    internal Dictionary<(string Type, object Id), List<object>> DeletedChildrenByParentRecursive =>
        _deletedChildrenByParentRecursive;

    // ========== MixedKeyComparer ==========

    private sealed class MixedKeyComparer : IEqualityComparer<(string Type, object Id)>
    {
        public static readonly MixedKeyComparer Instance = new();

        public bool Equals((string Type, object Id) x, (string Type, object Id) y)
            => x.Type == y.Type && (x.Id?.Equals(y.Id) ?? y.Id == null);

        public int GetHashCode((string Type, object Id) obj)
            => HashCode.Combine(obj.Type, obj.Id);
    }

    // ========== Performance Optimization ==========

    private Dictionary<IEntityType, List<EntityEntry>> GetOrBuildDeletedIndex() =>
        _deletedEntriesIndex ??= _context.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Deleted)
            .GroupBy(e => e.Metadata)
            .ToDictionary(g => g.Key, g => g.ToList());

    private void InvalidateDeletedIndex() => _deletedEntriesIndex = null;

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
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);
        var parentKey = CreateMixedKey(entry);

        CaptureDirectChildIds(entry, parentKey);
        CaptureDeletedChildrenFromTracker(entry, parentKey);

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
                CaptureChildIdsRecursive(item, currentDepth + 1, maxDepth, visited);
            }
        }
    }

    private void CaptureDirectChildIds(EntityEntry entry, (string Type, object Id) parentKey)
    {
        var childIds = new HashSet<(string Type, object Id)>(MixedKeyComparer.Instance);

        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                var childEntry = _context.Entry(item);
                var childKey = CreateMixedKey(childEntry);
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

    private void CaptureDeletedChildrenFromTracker(EntityEntry entry, (string Type, object Id) parentKey)
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
        NavigationEntry navigation, (string Type, object Id) parentKey)
    {
        var entityType = navigation.Metadata.TargetEntityType;
        var keyProperty = entityType.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (keyProperty == null)
        {
            return;
        }

        var fkProperty = NavigationPropertyHelper.GetForeignKeyProperty(navigation);
        if (fkProperty == null)
        {
            return;
        }

        var deletedIndex = GetOrBuildDeletedIndex();
        if (!deletedIndex.TryGetValue(entityType, out var deletedEntries))
        {
            return;
        }

        foreach (var trackedEntry in deletedEntries)
        {
            AddDeletedChildIfBelongsToParent(trackedEntry, fkProperty, parentKey, keyProperty);
        }
    }

    private void AddDeletedChildIfBelongsToParent(
        EntityEntry trackedEntry, IProperty fkProperty,
        (string Type, object Id) parentKey, IProperty keyProperty)
    {
        var fkValue = trackedEntry.Property(fkProperty.Name).CurrentValue;
        if (fkValue == null || !fkValue.Equals(parentKey.Id))
        {
            return;
        }

        var keyValue = trackedEntry.Property(keyProperty.Name).CurrentValue;
        if (keyValue == null)
        {
            return;
        }

        var childKey = (trackedEntry.Metadata.ClrType.Name, keyValue);

        if (!_originalChildIdsByParentRecursive.TryGetValue(parentKey, out var originalIds))
        {
            originalIds = new HashSet<(string Type, object Id)>(MixedKeyComparer.Instance);
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

    internal List<(string EntityType, object EntityId, int Depth)> GetOrphanedChildIdsRecursive(
        TEntity entity, int maxDepth)
    {
        var orphans = new List<(string EntityType, object EntityId, int Depth)>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        CollectOrphansRecursive(entity, 0, ClampDepth(maxDepth), visited, orphans);
        return orphans;
    }

    private void CollectOrphansRecursive(
        object entity, int currentDepth, int maxDepth,
        HashSet<object> visited, List<(string, object, int)> orphans)
    {
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);
        var parentKey = CreateMixedKey(entry);

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
        EntityEntry entry, (string Type, object Id) parentKey, int depth,
        List<(string, object, int)> orphans)
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

    private HashSet<(string Type, object Id)> GetCurrentChildKeysForEntity(EntityEntry entry)
    {
        var currentIds = new HashSet<(string Type, object Id)>(MixedKeyComparer.Instance);

        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                var childEntry = _context.Entry(item);
                currentIds.Add(CreateMixedKey(childEntry));
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

        var entityKey = _keyService.GetEntityKey(entity);
        var summary = string.Join(", ", orphanedIds.Select(o => $"{o.EntityType}:{o.EntityId}@depth{o.Depth}"));
        throw new InvalidOperationException(
            $"Entity {typeof(TEntity).Name} (Id={entityKey}) has {orphanedIds.Count} orphaned descendant(s): " +
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
        var parentKey = CreateMixedKey(entry);

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

    private void DetachDeletedChildrenFromTracker((string Type, object Id) parentKey)
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
        var parentKey = CreateMixedKey(entry);

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

    private void ReattachDeletedChildrenAsDeleted((string Type, object Id) parentKey)
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

    internal void DetachAllDeletedChildrenRecursive()
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

    private (string Type, object Id) CreateMixedKey(EntityEntry entry)
    {
        var key = _keyService.GetEntityKey(entry);
        return (entry.Metadata.ClrType.Name, key.GetValueAsObject());
    }

    private static int ClampDepth(int maxDepth) => Math.Min(maxDepth, AbsoluteMaxDepth);
}
