using EfCoreUtils.Internal.Visitors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EfCoreUtils.Internal.Services;

/// <summary>
/// Tracks reference entities processed during graph traversal.
/// </summary>
internal class ReferenceTrackingResult
{
    internal Dictionary<string, List<object>> ProcessedReferencesByType { get; } = [];
    internal int MaxReferenceDepthReached { get; set; }

    internal void AddReference(string typeName, object entityId, int depth)
    {
        if (!ProcessedReferencesByType.TryGetValue(typeName, out var list))
        {
            list = [];
            ProcessedReferencesByType[typeName] = list;
        }
        list.Add(entityId);
        MaxReferenceDepthReached = Math.Max(MaxReferenceDepthReached, depth);
    }

    internal int UniqueReferencesProcessed =>
        ProcessedReferencesByType.Values.Sum(list => list.Count);
}

internal class EntityAttachmentService<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly DbContext _context;
    private readonly GraphTraversalEngine _engine;

    internal EntityAttachmentService(DbContext context)
    {
        _context = context;
        _engine = new GraphTraversalEngine(context);
    }

    internal void AttachEntityAsDeleted(TEntity entity) => _context.Entry(entity).State = EntityState.Deleted;

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

            AttachCollectionChildrenAsState(navigation, EntityState.Added);
        }
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

            AttachCollectionChildrenAsState(navigation, EntityState.Modified);
        }
    }

    internal void AttachEntityGraphAsDeleted(TEntity entity)
    {
        var entry = _context.Entry(entity);

        foreach (var navigation in entry.Navigations)
        {
            if (navigation.CurrentValue == null || !navigation.Metadata.IsCollection)
            {
                continue;
            }

            MarkCollectionChildrenAsDeleted(navigation);
        }

        entry.State = EntityState.Deleted;
    }

    private void AttachCollectionChildrenAsState(NavigationEntry navigation, EntityState state)
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
                itemEntry.State = state;
            }
        }
    }

    private void MarkCollectionChildrenAsDeleted(NavigationEntry navigation)
    {
        if (navigation.CurrentValue is not System.Collections.IEnumerable collection)
        {
            return;
        }

        var items = collection.Cast<object>().ToList();
        foreach (var item in items)
        {
            _context.Entry(item).State = EntityState.Deleted;
        }
    }

    // ========== Recursive Attachment Methods ==========

    // Traversal options that match original behavior - traverse all collections
    private static readonly TraversalOptions AttachOptions = new() { SkipManyToMany = false };
    private static readonly TraversalOptions AttachDeleteOptions = new() { BottomUp = true, SkipManyToMany = false };

    internal void AttachEntityGraphAsAddedRecursive(TEntity entity, int maxDepth)
    {
        var visitor = new ConditionalStateVisitor();
        var ctx = new StateVisitorContext { TargetState = EntityState.Added, OnlyIfDetached = false };
        _engine.Traverse(entity, maxDepth, visitor, ctx, AttachOptions);
    }

    internal void AttachEntityGraphAsModifiedRecursive(TEntity entity, int maxDepth)
    {
        var visitor = new ConditionalStateVisitor();
        var ctx = new StateVisitorContext { TargetState = EntityState.Modified, OnlyIfDetached = true };
        _engine.Traverse(entity, maxDepth, visitor, ctx, AttachOptions);
    }

    // CRITICAL: Delete uses bottom-up order - children before parent for FK constraints
    internal void AttachEntityGraphAsDeletedRecursive(TEntity entity, int maxDepth)
    {
        var visitor = new EntityStateVisitor();
        _engine.Traverse(entity, maxDepth, visitor, EntityState.Deleted, AttachDeleteOptions);
    }

    private static int ClampDepth(int maxDepth) => DepthConstants.ClampDepth(maxDepth);

    // ========== Reference-Aware Attachment Methods ==========

    internal ReferenceTrackingResult AttachEntityGraphAsAddedWithReferences(
        TEntity entity, int maxDepth, CircularReferenceHandling circularHandling)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var referenceResult = new ReferenceTrackingResult();
        AttachAsAddedWithReferences(entity, 0, ClampDepth(maxDepth), visited, circularHandling, referenceResult);
        return referenceResult;
    }

    private void AttachAsAddedWithReferences(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited,
        CircularReferenceHandling circularHandling, ReferenceTrackingResult refResult)
    {
        if (!TryVisitEntity(entity, visited, circularHandling, currentDepth))
        {
            return;
        }

        var entry = _context.Entry(entity);
        entry.State = EntityState.Added;

        if (currentDepth >= maxDepth)
        {
            return;
        }

        AttachChildrenWithReferences(entry, currentDepth, maxDepth, visited,
            circularHandling, refResult, EntityState.Added);
        AttachReferences(entry, currentDepth, maxDepth, visited,
            circularHandling, refResult, EntityState.Added);
    }

    internal ReferenceTrackingResult AttachEntityGraphAsModifiedWithReferences(
        TEntity entity, int maxDepth, CircularReferenceHandling circularHandling)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var referenceResult = new ReferenceTrackingResult();
        AttachAsModifiedWithReferences(entity, 0, ClampDepth(maxDepth), visited, circularHandling, referenceResult);
        return referenceResult;
    }

    private void AttachAsModifiedWithReferences(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited,
        CircularReferenceHandling circularHandling, ReferenceTrackingResult refResult)
    {
        if (!TryVisitEntity(entity, visited, circularHandling, currentDepth))
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

        AttachChildrenWithReferences(entry, currentDepth, maxDepth, visited,
            circularHandling, refResult, EntityState.Modified);
        AttachReferences(entry, currentDepth, maxDepth, visited,
            circularHandling, refResult, EntityState.Modified);
    }

    private bool TryVisitEntity(
        object entity, HashSet<object> visited,
        CircularReferenceHandling circularHandling, int currentDepth)
    {
        if (visited.Add(entity))
        {
            return true;
        }

        if (circularHandling == CircularReferenceHandling.Throw)
        {
            var entry = _context.Entry(entity);
            var entityType = entry.Metadata.ClrType.Name;
            var entityId = GetEntityIdFromEntry(entry);
            throw new InvalidOperationException(
                $"Circular reference detected: Entity '{entityType}' (Id={entityId}) at depth {currentDepth} " +
                $"was already visited. Set CircularReferenceHandling to Ignore to process each entity once.");
        }

        return false;
    }

    private static object GetEntityIdFromEntry(EntityEntry entry)
    {
        var keyProperty = entry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (keyProperty == null)
        {
            return "unknown";
        }
        return entry.Property(keyProperty.Name).CurrentValue ?? "unknown";
    }

    private void AttachChildrenWithReferences(
        EntityEntry entry, int currentDepth, int maxDepth, HashSet<object> visited,
        CircularReferenceHandling circularHandling, ReferenceTrackingResult refResult,
        EntityState targetState)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                AttachChildItemWithReferences(
                    item, currentDepth + 1, maxDepth, visited, circularHandling, refResult, targetState);
            }
        }
    }

    private void AttachChildItemWithReferences(
        object item, int newDepth, int maxDepth, HashSet<object> visited,
        CircularReferenceHandling circularHandling, ReferenceTrackingResult refResult,
        EntityState targetState)
    {
        if (targetState == EntityState.Added)
        {
            AttachAsAddedWithReferences(item, newDepth, maxDepth, visited, circularHandling, refResult);
        }
        else
        {
            AttachAsModifiedWithReferences(item, newDepth, maxDepth, visited, circularHandling, refResult);
        }
    }

    private void AttachReferences(
        EntityEntry entry, int currentDepth, int maxDepth, HashSet<object> visited,
        CircularReferenceHandling circularHandling, ReferenceTrackingResult refResult,
        EntityState targetState)
    {
        foreach (var navigation in NavigationPropertyHelper.GetReferenceNavigations(entry))
        {
            var refEntity = NavigationPropertyHelper.GetReferenceValue(navigation);
            if (refEntity == null || !TryVisitEntity(refEntity, visited, circularHandling, currentDepth + 1))
            {
                continue;
            }

            ProcessReferenceEntity(
                refEntity, currentDepth, maxDepth, visited, circularHandling, refResult, targetState);
        }
    }

    private void ProcessReferenceEntity(
        object refEntity, int currentDepth, int maxDepth, HashSet<object> visited,
        CircularReferenceHandling circularHandling, ReferenceTrackingResult refResult,
        EntityState targetState)
    {
        var refEntry = _context.Entry(refEntity);
        TrackReference(refEntry, refResult, currentDepth + 1);

        if (refEntry.State == EntityState.Detached)
        {
            refEntry.State = targetState;
        }

        if (currentDepth + 1 >= maxDepth)
        {
            return;
        }

        AttachChildrenWithReferences(
            refEntry, currentDepth + 1, maxDepth, visited, circularHandling, refResult, targetState);
        AttachReferences(
            refEntry, currentDepth + 1, maxDepth, visited, circularHandling, refResult, targetState);
    }

    private void TrackReference(EntityEntry entry, ReferenceTrackingResult refResult, int depth)
    {
        var typeName = entry.Metadata.ClrType.Name;
        var entityId = GetEntityIdFromEntry(entry);
        refResult.AddReference(typeName, entityId, depth);
    }
}
