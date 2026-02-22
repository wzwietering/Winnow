using Winnow.Internal.Visitors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Winnow.Internal.Services;

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

    private static TraversalOptions CreateAttachOptions(NavigationFilter? filter) =>
        new() { SkipManyToMany = false, NavigationFilter = filter };

    private static TraversalOptions CreateAttachDeleteOptions(NavigationFilter? filter) =>
        new() { BottomUp = true, SkipManyToMany = false, NavigationFilter = filter };

    internal void AttachEntityGraphAsAddedRecursive(TEntity entity, TraversalContext tc)
    {
        var visitor = new ConditionalStateVisitor();
        var ctx = new StateVisitorContext { TargetState = EntityState.Added, OnlyIfDetached = false };
        _engine.Traverse(entity, tc.MaxDepth, visitor, ctx, CreateAttachOptions(tc.NavigationFilter));
    }

    internal void AttachEntityGraphAsModifiedRecursive(TEntity entity, TraversalContext tc)
    {
        var visitor = new ConditionalStateVisitor();
        var ctx = new StateVisitorContext { TargetState = EntityState.Modified, OnlyIfDetached = true };
        _engine.Traverse(entity, tc.MaxDepth, visitor, ctx, CreateAttachOptions(tc.NavigationFilter));
    }

    // CRITICAL: Delete uses bottom-up order - children before parent for FK constraints
    internal void AttachEntityGraphAsDeletedRecursive(TEntity entity, TraversalContext tc)
    {
        var visitor = new EntityStateVisitor();
        _engine.Traverse(entity, tc.MaxDepth, visitor, EntityState.Deleted, CreateAttachDeleteOptions(tc.NavigationFilter));
    }

    private static int ClampDepth(int maxDepth) => DepthConstants.ClampDepth(maxDepth);

    // ========== Reference-Aware Attachment Methods ==========

    internal ReferenceTrackingResult AttachEntityGraphAsAddedWithReferences(
        TEntity entity, TraversalContext tc)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var referenceResult = new ReferenceTrackingResult();
        AttachWithStateAndReferences(
            entity, 0, ClampDepth(tc.MaxDepth), visited, tc.CircularReferenceHandling,
            referenceResult, EntityState.Added, tc.NavigationFilter);
        return referenceResult;
    }

    internal ReferenceTrackingResult AttachEntityGraphAsModifiedWithReferences(
        TEntity entity, TraversalContext tc)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var referenceResult = new ReferenceTrackingResult();
        AttachWithStateAndReferences(
            entity, 0, ClampDepth(tc.MaxDepth), visited, tc.CircularReferenceHandling,
            referenceResult, EntityState.Modified, tc.NavigationFilter);
        return referenceResult;
    }

    private void AttachWithStateAndReferences(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited,
        CircularReferenceHandling circularHandling, ReferenceTrackingResult refResult,
        EntityState targetState, NavigationFilter? filter)
    {
        if (!TryVisitEntity(entity, visited, circularHandling, currentDepth))
        {
            return;
        }

        var entry = _context.Entry(entity);
        SetEntityStateForAttach(entry, targetState);

        if (currentDepth >= maxDepth)
        {
            return;
        }

        TraverseChildrenAndReferences(
            entry, currentDepth, maxDepth, visited,
            circularHandling, refResult, targetState, filter);
    }

    private static void SetEntityStateForAttach(EntityEntry entry, EntityState targetState)
    {
        if (targetState == EntityState.Added)
        {
            entry.State = EntityState.Added;
            return;
        }

        if (entry.State == EntityState.Detached)
        {
            entry.State = targetState;
        }
    }

    private static void SetEntityStateIfDetached(EntityEntry entry, EntityState targetState)
    {
        if (entry.State == EntityState.Detached)
        {
            entry.State = targetState;
        }
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
        try
        {
            var keyProperties = entry.Metadata.FindPrimaryKey()?.Properties;
            if (keyProperties == null || keyProperties.Count == 0)
            {
                return "unknown";
            }

            if (keyProperties.Count == 1)
            {
                return entry.Property(keyProperties[0].Name).CurrentValue ?? "unknown";
            }

            var values = keyProperties
                .Select(p => entry.Property(p.Name).CurrentValue ?? "null")
                .ToArray();
            return $"({string.Join(", ", values)})";
        }
        catch
        {
            return "unknown";
        }
    }

    private void TraverseChildrenAndReferences(
        EntityEntry entry, int currentDepth, int maxDepth, HashSet<object> visited,
        CircularReferenceHandling circularHandling, ReferenceTrackingResult refResult,
        EntityState targetState, NavigationFilter? filter)
    {
        TraverseCollections(entry, currentDepth, maxDepth, visited,
            circularHandling, refResult, targetState, filter);
        TraverseReferences(entry, currentDepth, maxDepth, visited,
            circularHandling, refResult, targetState, filter);
    }

    private void TraverseCollections(
        EntityEntry entry, int currentDepth, int maxDepth, HashSet<object> visited,
        CircularReferenceHandling circularHandling, ReferenceTrackingResult refResult,
        EntityState targetState, NavigationFilter? filter)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!TraversalHelper.ShouldTraverseCollection(navigation, filter, skipManyToMany: false))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                AttachWithStateAndReferences(
                    item, currentDepth + 1, maxDepth, visited, circularHandling, refResult, targetState, filter);
            }
        }
    }

    private void TraverseReferences(
        EntityEntry entry, int currentDepth, int maxDepth, HashSet<object> visited,
        CircularReferenceHandling circularHandling, ReferenceTrackingResult refResult,
        EntityState targetState, NavigationFilter? filter)
    {
        foreach (var navigation in NavigationPropertyHelper.GetReferenceNavigations(entry))
        {
            if (!TraversalHelper.ShouldTraverseReference(navigation, filter))
            {
                continue;
            }

            var refEntity = NavigationPropertyHelper.GetReferenceValue(navigation);
            if (refEntity == null || !TryVisitEntity(refEntity, visited, circularHandling, currentDepth + 1))
            {
                continue;
            }

            ProcessReference(refEntity, currentDepth, maxDepth, visited,
                circularHandling, refResult, targetState, filter);
        }
    }

    private void ProcessReference(
        object refEntity, int currentDepth, int maxDepth, HashSet<object> visited,
        CircularReferenceHandling circularHandling, ReferenceTrackingResult refResult,
        EntityState targetState, NavigationFilter? filter)
    {
        var refEntry = _context.Entry(refEntity);
        TrackReference(refEntry, refResult, currentDepth + 1);
        SetEntityStateIfDetached(refEntry, targetState);

        if (currentDepth + 1 < maxDepth)
        {
            TraverseChildrenAndReferences(refEntry, currentDepth + 1, maxDepth, visited,
                circularHandling, refResult, targetState, filter);
        }
    }

    private void TrackReference(EntityEntry entry, ReferenceTrackingResult refResult, int depth)
    {
        var typeName = entry.Metadata.ClrType.Name;
        var entityId = GetEntityIdFromEntry(entry);
        refResult.AddReference(typeName, entityId, depth);
    }

    // ========== Upsert Attachment Methods ==========

    private static void SetUpsertStateIfDetached<TValidationEntity, TValidationKey>(
        EntityEntry entry, ValidationService<TValidationEntity, TValidationKey> validationService)
        where TValidationEntity : class
        where TValidationKey : notnull, IEquatable<TValidationKey>
    {
        if (entry.State != EntityState.Detached)
        {
            return;
        }

        var isNew = validationService.HasDefaultKeyValueForEntry(entry);
        entry.State = isNew ? EntityState.Added : EntityState.Modified;
    }

    internal void AttachEntityGraphAsUpsertRecursive<TValidationEntity, TValidationKey>(
        TEntity entity, TraversalContext tc,
        ValidationService<TValidationEntity, TValidationKey> validationService)
        where TValidationEntity : class
        where TValidationKey : notnull, IEquatable<TValidationKey>
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        AttachAsUpsertRecursive(entity, 0, ClampDepth(tc.MaxDepth), visited, validationService, tc.NavigationFilter);
    }

    private void AttachAsUpsertRecursive<TValidationEntity, TValidationKey>(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited,
        ValidationService<TValidationEntity, TValidationKey> validationService,
        NavigationFilter? filter)
        where TValidationEntity : class
        where TValidationKey : notnull, IEquatable<TValidationKey>
    {
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);
        var isNew = validationService.HasDefaultKeyValueForEntry(entry);
        entry.State = isNew ? EntityState.Added : EntityState.Modified;

        if (currentDepth >= maxDepth)
        {
            return;
        }

        AttachUpsertChildren(entry, currentDepth, maxDepth, visited, validationService, filter);
    }

    private void AttachUpsertChildren<TValidationEntity, TValidationKey>(
        EntityEntry entry, int currentDepth, int maxDepth, HashSet<object> visited,
        ValidationService<TValidationEntity, TValidationKey> validationService,
        NavigationFilter? filter)
        where TValidationEntity : class
        where TValidationKey : notnull, IEquatable<TValidationKey>
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!TraversalHelper.ShouldTraverseCollection(navigation, filter, skipManyToMany: false))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                AttachAsUpsertRecursive(item, currentDepth + 1, maxDepth, visited, validationService, filter);
            }
        }
    }

    internal ReferenceTrackingResult AttachEntityGraphAsUpsertWithReferences<TValidationEntity, TValidationKey>(
        TEntity entity, TraversalContext tc,
        ValidationService<TValidationEntity, TValidationKey> validationService)
        where TValidationEntity : class
        where TValidationKey : notnull, IEquatable<TValidationKey>
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var referenceResult = new ReferenceTrackingResult();
        AttachAsUpsertWithReferences(
            entity, 0, ClampDepth(tc.MaxDepth), visited, tc.CircularReferenceHandling, referenceResult,
            validationService, tc.NavigationFilter);
        return referenceResult;
    }

    private void AttachAsUpsertWithReferences<TValidationEntity, TValidationKey>(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited,
        CircularReferenceHandling circularHandling, ReferenceTrackingResult refResult,
        ValidationService<TValidationEntity, TValidationKey> validationService,
        NavigationFilter? filter)
        where TValidationEntity : class
        where TValidationKey : notnull, IEquatable<TValidationKey>
    {
        if (!TryVisitEntity(entity, visited, circularHandling, currentDepth))
        {
            return;
        }

        var entry = _context.Entry(entity);
        var isNew = validationService.HasDefaultKeyValueForEntry(entry);
        entry.State = isNew ? EntityState.Added : EntityState.Modified;

        if (currentDepth >= maxDepth)
        {
            return;
        }

        AttachUpsertChildrenWithReferences(
            entry, currentDepth, maxDepth, visited, circularHandling, refResult, validationService, filter);
        AttachUpsertReferences(
            entry, currentDepth, maxDepth, visited, circularHandling, refResult, validationService, filter);
    }

    private void AttachUpsertChildrenWithReferences<TValidationEntity, TValidationKey>(
        EntityEntry entry, int currentDepth, int maxDepth, HashSet<object> visited,
        CircularReferenceHandling circularHandling, ReferenceTrackingResult refResult,
        ValidationService<TValidationEntity, TValidationKey> validationService,
        NavigationFilter? filter)
        where TValidationEntity : class
        where TValidationKey : notnull, IEquatable<TValidationKey>
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!TraversalHelper.ShouldTraverseCollection(navigation, filter, skipManyToMany: false))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                AttachAsUpsertWithReferences(
                    item, currentDepth + 1, maxDepth, visited, circularHandling, refResult, validationService, filter);
            }
        }
    }

    private void AttachUpsertReferences<TValidationEntity, TValidationKey>(
        EntityEntry entry, int currentDepth, int maxDepth, HashSet<object> visited,
        CircularReferenceHandling circularHandling, ReferenceTrackingResult refResult,
        ValidationService<TValidationEntity, TValidationKey> validationService,
        NavigationFilter? filter)
        where TValidationEntity : class
        where TValidationKey : notnull, IEquatable<TValidationKey>
    {
        foreach (var navigation in NavigationPropertyHelper.GetReferenceNavigations(entry))
        {
            if (!TraversalHelper.ShouldTraverseReference(navigation, filter))
            {
                continue;
            }

            var refEntity = NavigationPropertyHelper.GetReferenceValue(navigation);
            if (refEntity == null || !TryVisitEntity(refEntity, visited, circularHandling, currentDepth + 1))
            {
                continue;
            }

            ProcessUpsertReferenceEntity(
                refEntity, currentDepth, maxDepth, visited, circularHandling, refResult, validationService, filter);
        }
    }

    private void ProcessUpsertReferenceEntity<TValidationEntity, TValidationKey>(
        object refEntity, int currentDepth, int maxDepth, HashSet<object> visited,
        CircularReferenceHandling circularHandling, ReferenceTrackingResult refResult,
        ValidationService<TValidationEntity, TValidationKey> validationService,
        NavigationFilter? filter)
        where TValidationEntity : class
        where TValidationKey : notnull, IEquatable<TValidationKey>
    {
        var refEntry = _context.Entry(refEntity);
        TrackReference(refEntry, refResult, currentDepth + 1);
        SetUpsertStateIfDetached(refEntry, validationService);

        if (currentDepth + 1 >= maxDepth)
        {
            return;
        }

        AttachUpsertChildrenWithReferences(
            refEntry, currentDepth + 1, maxDepth, visited, circularHandling, refResult, validationService, filter);
        AttachUpsertReferences(
            refEntry, currentDepth + 1, maxDepth, visited, circularHandling, refResult, validationService, filter);
    }
}
