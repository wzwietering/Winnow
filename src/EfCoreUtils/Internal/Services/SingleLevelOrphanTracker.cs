using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EfCoreUtils.Internal.Services;

internal class SingleLevelOrphanTracker<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly DbContext _context;
    private readonly EntityKeyService<TEntity, TKey> _keyService;
    private readonly Func<Dictionary<IEntityType, List<EntityEntry>>> _getDeletedIndex;
    private readonly Action _invalidateDeletedIndex;

    private readonly Dictionary<TKey, HashSet<TKey>> _originalChildIdsByParent = [];
    private readonly Dictionary<TKey, List<object>> _deletedChildrenByParent = [];

    internal SingleLevelOrphanTracker(
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

    internal Dictionary<TKey, List<object>> DeletedChildrenByParent => _deletedChildrenByParent;

    internal void CaptureAllOriginalChildIds(List<TEntity> entities)
    {
        _context.ChangeTracker.DetectChanges();
        _invalidateDeletedIndex();

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

    private (HashSet<TKey> childIds, List<object> deleted) CollectChildrenAndDeleted(
        TEntity entity, TKey parentId)
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
        var deletedIndex = _getDeletedIndex();
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

    internal void DetachEntityWithOrphans(
        TEntity entity, EntityDetachmentService<TEntity, TKey> detachmentService)
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
}
