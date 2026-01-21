using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EfCoreUtils.Internal.Services;

/// <summary>
/// Service for tracking changes to many-to-many relationships during updates.
/// Captures original links and detects additions/removals.
/// </summary>
internal class LinkChangeTrackingService<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly DbContext _context;
    private readonly EntityKeyService<TEntity, TKey> _keyService;
    private readonly Dictionary<(Type EntityType, object EntityId), Dictionary<string, HashSet<object>>> _originalLinks = [];

    internal LinkChangeTrackingService(DbContext context, EntityKeyService<TEntity, TKey> keyService)
    {
        _context = context;
        _keyService = keyService;
    }

    internal void CaptureOriginalLinks(IEnumerable<TEntity> entities, int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(entities);

        if (maxDepth < 0 || maxDepth > DepthConstants.AbsoluteMaxDepth)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth),
                $"maxDepth must be between 0 and {DepthConstants.AbsoluteMaxDepth}");
        }

        _originalLinks.Clear();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach (var entity in entities)
        {
            CaptureEntityLinks(entity, visited, 0, maxDepth);
        }
    }

    internal ManyToManyStatisticsTracker ApplyLinkChanges(TEntity entity, GraphBatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxDepth < 0 || options.MaxDepth > DepthConstants.AbsoluteMaxDepth)
        {
            throw new ArgumentOutOfRangeException(nameof(options),
                $"MaxDepth must be between 0 and {DepthConstants.AbsoluteMaxDepth}");
        }

        var tracker = new ManyToManyStatisticsTracker();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        ApplyEntityLinkChanges(entity, tracker, visited, 0, options.MaxDepth, options.MaxManyToManyCollectionSize);
        return tracker;
    }

    private void CaptureEntityLinks(object entity, HashSet<object> visited, int depth, int maxDepth)
    {
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);
        CaptureEntryLinks(entry);

        if (depth >= maxDepth)
        {
            return;
        }

        CaptureChildLinks(entry, visited, depth, maxDepth);
    }

    private void CaptureEntryLinks(EntityEntry entry)
    {
        var entityType = entry.Metadata.ClrType;
        var entityId = EntityEntryHelper.GetEntityIdSafe(entry);
        var key = (entityType, entityId);

        if (_originalLinks.ContainsKey(key))
        {
            return;
        }

        var linksByNavigation = new Dictionary<string, HashSet<object>>();

        foreach (var navigation in ManyToManyNavigationHelper.GetManyToManyNavigations(entry))
        {
            var navName = navigation.Metadata.Name;
            var relatedIds = CaptureRelatedIds(navigation);
            linksByNavigation[navName] = relatedIds;
        }

        _originalLinks[key] = linksByNavigation;
    }

    private HashSet<object> CaptureRelatedIds(NavigationEntry navigation)
    {
        var ids = new HashSet<object>();
        var targetType = navigation.Metadata.TargetEntityType;
        var keyProperty = targetType.FindPrimaryKey()?.Properties.FirstOrDefault();

        if (keyProperty == null)
        {
            return ids;
        }

        foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
        {
            var itemEntry = _context.Entry(item);
            var idValue = itemEntry.Property(keyProperty.Name).CurrentValue;
            if (idValue != null)
            {
                ids.Add(idValue);
            }
        }

        return ids;
    }

    private void CaptureChildLinks(EntityEntry entry, HashSet<object> visited, int depth, int maxDepth)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            if (ManyToManyNavigationHelper.IsManyToManyNavigation(navigation))
            {
                continue;
            }

            foreach (var child in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                CaptureEntityLinks(child, visited, depth + 1, maxDepth);
            }
        }
    }

    private void ApplyEntityLinkChanges(
        object entity, ManyToManyStatisticsTracker tracker,
        HashSet<object> visited, int depth, int maxDepth, int maxCollectionSize)
    {
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);
        ApplyEntryLinkChanges(entry, tracker, maxCollectionSize);

        if (depth >= maxDepth)
        {
            return;
        }

        ApplyChildLinkChanges(entry, tracker, visited, depth, maxDepth, maxCollectionSize);
    }

    private void ApplyEntryLinkChanges(EntityEntry entry, ManyToManyStatisticsTracker tracker, int maxCollectionSize)
    {
        var entityType = entry.Metadata.ClrType;
        var entityId = EntityEntryHelper.GetEntityIdSafe(entry);
        var key = (entityType, entityId);
        var entityTypeName = entityType.Name;

        if (!_originalLinks.TryGetValue(key, out var originalByNav))
        {
            return;
        }

        foreach (var navigation in ManyToManyNavigationHelper.GetManyToManyNavigations(entry))
        {
            var navName = navigation.Metadata.Name;
            var currentIds = CaptureRelatedIds(navigation);

            ManyToManyValidation.ValidateCollectionSize(entityTypeName, navName, currentIds.Count, maxCollectionSize);

            if (!originalByNav.TryGetValue(navName, out var originalIds))
            {
                originalIds = [];
            }

            var (added, removed) = DetectChanges(originalIds, currentIds);

            RecordChanges(navigation, added, removed, tracker, entityTypeName, navName);
        }
    }

    private static (HashSet<object> Added, HashSet<object> Removed) DetectChanges(
        HashSet<object> original, HashSet<object> current)
    {
        var added = new HashSet<object>(current);
        added.ExceptWith(original);

        var removed = new HashSet<object>(original);
        removed.ExceptWith(current);

        return (added, removed);
    }

    private void RecordChanges(
        NavigationEntry navigation,
        HashSet<object> added, HashSet<object> removed,
        ManyToManyStatisticsTracker tracker, string entityTypeName, string navName)
    {
        foreach (var _ in added)
        {
            tracker.RecordJoinCreated(entityTypeName, navName);
        }

        if (removed.Count > 0)
        {
            HandleRemovedLinks(navigation, removed, tracker, entityTypeName, navName);
        }
    }

    private void HandleRemovedLinks(
        NavigationEntry navigation, HashSet<object> removedIds,
        ManyToManyStatisticsTracker tracker, string entityTypeName, string navName)
    {
        if (ManyToManyNavigationHelper.IsSkipNavigation(navigation))
        {
            foreach (var _ in removedIds)
            {
                tracker.RecordJoinRemoved(entityTypeName, navName);
            }
        }
        else
        {
            MarkRemovedExplicitJoinsAsDeleted(navigation, removedIds, tracker, entityTypeName, navName);
        }
    }

    private void MarkRemovedExplicitJoinsAsDeleted(
        NavigationEntry navigation, HashSet<object> removedIds,
        ManyToManyStatisticsTracker tracker, string entityTypeName, string navName)
    {
        var targetType = navigation.Metadata.TargetEntityType;
        var keyProperty = targetType.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (keyProperty == null)
        {
            return;
        }

        foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
        {
            var itemEntry = _context.Entry(item);
            var idValue = itemEntry.Property(keyProperty.Name).CurrentValue;

            if (idValue != null && removedIds.Contains(idValue))
            {
                itemEntry.State = EntityState.Deleted;
                tracker.RecordJoinRemoved(entityTypeName, navName);
            }
        }
    }

    private void ApplyChildLinkChanges(
        EntityEntry entry, ManyToManyStatisticsTracker tracker,
        HashSet<object> visited, int depth, int maxDepth, int maxCollectionSize)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            if (ManyToManyNavigationHelper.IsManyToManyNavigation(navigation))
            {
                continue;
            }

            foreach (var child in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                ApplyEntityLinkChanges(child, tracker, visited, depth + 1, maxDepth, maxCollectionSize);
            }
        }
    }
}
