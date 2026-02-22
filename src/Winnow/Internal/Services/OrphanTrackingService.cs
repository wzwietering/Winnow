using Winnow.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Winnow.Internal.Services;

internal class OrphanTrackingService<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly SingleLevelOrphanTracker<TEntity, TKey> _singleLevelTracker;
    private readonly RecursiveOrphanTracker<TEntity, TKey> _recursiveTracker;

    private Dictionary<IEntityType, List<EntityEntry>>? _deletedEntriesIndex;
    private readonly DbContext _context;

    internal OrphanTrackingService(DbContext context, EntityKeyService<TEntity, TKey> keyService)
    {
        _context = context;
        _singleLevelTracker = new SingleLevelOrphanTracker<TEntity, TKey>(
            context, keyService, GetOrBuildDeletedIndex, InvalidateDeletedIndex);
        _recursiveTracker = new RecursiveOrphanTracker<TEntity, TKey>(
            context, keyService, GetOrBuildDeletedIndex, InvalidateDeletedIndex);
    }

    internal Dictionary<TKey, List<object>> DeletedChildrenByParent =>
        _singleLevelTracker.DeletedChildrenByParent;

    internal Dictionary<(string Type, TKey Id), List<object>> DeletedChildrenByParentRecursive =>
        _recursiveTracker.DeletedChildrenByParentRecursive;

    // Single-level operations
    internal void CaptureAllOriginalChildIds(List<TEntity> entities) =>
        _singleLevelTracker.CaptureAllOriginalChildIds(entities);

    internal List<TKey> GetChildIds(TEntity entity) =>
        _singleLevelTracker.GetChildIds(entity);

    internal List<TKey> GetOrphanedChildIds(TEntity entity) =>
        _singleLevelTracker.GetOrphanedChildIds(entity);

    internal void ValidateNoOrphanedChildren(TEntity entity, GraphBatchOptions options) =>
        _singleLevelTracker.ValidateNoOrphanedChildren(entity, options);

    internal void HandleOrphanedChildren(TEntity entity, GraphBatchOptions options) =>
        _singleLevelTracker.HandleOrphanedChildren(entity, options);

    internal void DetachEntityWithOrphans(
        TEntity entity, EntityDetachmentService<TEntity, TKey> detachmentService) =>
        _singleLevelTracker.DetachEntityWithOrphans(entity, detachmentService);

    // Recursive operations
    internal void CaptureAllOriginalChildIdsRecursive(List<TEntity> entities, TraversalContext tc) =>
        _recursiveTracker.CaptureAllOriginalChildIdsRecursive(entities, tc);

    internal void ValidateNoOrphanedChildrenRecursive(
        TEntity entity, TraversalContext tc, GraphBatchOptions options) =>
        _recursiveTracker.ValidateNoOrphanedChildrenRecursive(entity, tc, options);

    internal void HandleOrphanedChildrenRecursive(
        TEntity entity, TraversalContext tc, OrphanBehavior behavior) =>
        _recursiveTracker.HandleOrphanedChildrenRecursive(entity, tc, behavior);

    internal void DetachEntityWithOrphansRecursive(
        TEntity entity, TraversalContext tc,
        EntityDetachmentService<TEntity, TKey> detachmentService) =>
        _recursiveTracker.DetachEntityWithOrphansRecursive(entity, tc, detachmentService);

    // Shared index management
    private Dictionary<IEntityType, List<EntityEntry>> GetOrBuildDeletedIndex() =>
        _deletedEntriesIndex ??= _context.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Deleted)
            .GroupBy(e => e.Metadata)
            .ToDictionary(g => g.Key, g => g.ToList());

    private void InvalidateDeletedIndex() => _deletedEntriesIndex = null;
}
