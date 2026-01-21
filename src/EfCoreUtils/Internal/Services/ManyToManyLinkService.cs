using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EfCoreUtils.Internal.Services;

/// <summary>
/// Service for managing many-to-many join records during graph operations.
/// Handles both skip navigations (EF-managed) and explicit join entities.
/// </summary>
internal class ManyToManyLinkService<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly DbContext _context;
    private readonly EntityKeyService<TEntity, TKey> _keyService;

    internal ManyToManyLinkService(DbContext context, EntityKeyService<TEntity, TKey> keyService)
    {
        _context = context;
        _keyService = keyService;
    }

    internal ManyToManyStatisticsTracker ProcessManyToManyForInsert(
        TEntity entity, InsertGraphBatchOptions options)
    {
        var tracker = new ManyToManyStatisticsTracker();
        var entry = _context.Entry(entity);
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { entity };

        ProcessEntityManyToMany(entry, options, tracker, visited, 0, options.MaxDepth);
        return tracker;
    }

    internal ManyToManyStatisticsTracker ProcessManyToManyForDelete(TEntity entity)
    {
        var tracker = new ManyToManyStatisticsTracker();
        var entry = _context.Entry(entity);

        RemoveJoinRecords(entry, tracker);
        return tracker;
    }

    private void ProcessEntityManyToMany(
        EntityEntry entry, InsertGraphBatchOptions options,
        ManyToManyStatisticsTracker tracker, HashSet<object> visited,
        int currentDepth, int maxDepth)
    {
        foreach (var navigation in ManyToManyNavigationHelper.GetManyToManyNavigations(entry))
        {
            ProcessManyToManyNavigation(entry, navigation, options, tracker);
        }

        if (currentDepth >= maxDepth)
        {
            return;
        }

        ProcessChildrenManyToMany(entry, options, tracker, visited, currentDepth, maxDepth);
    }

    private void ProcessChildrenManyToMany(
        EntityEntry entry, InsertGraphBatchOptions options,
        ManyToManyStatisticsTracker tracker, HashSet<object> visited,
        int currentDepth, int maxDepth)
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
                if (!visited.Add(child))
                {
                    continue;
                }

                var childEntry = _context.Entry(child);
                ProcessEntityManyToMany(childEntry, options, tracker, visited, currentDepth + 1, maxDepth);
            }
        }
    }

    private void ProcessManyToManyNavigation(
        EntityEntry entry, NavigationEntry navigation,
        InsertGraphBatchOptions options, ManyToManyStatisticsTracker tracker)
    {
        var entityTypeName = entry.Metadata.ClrType.Name;
        var navigationName = navigation.Metadata.Name;

        if (options.ValidateManyToManyEntitiesExist &&
            options.ManyToManyInsertBehavior == ManyToManyInsertBehavior.AttachExisting)
        {
            ValidateRelatedEntitiesExist(entry, navigation);
        }

        AttachRelatedEntities(navigation, options.ManyToManyInsertBehavior);

        var itemCount = CountNavigationItems(navigation);
        for (var i = 0; i < itemCount; i++)
        {
            tracker.RecordJoinCreated(entityTypeName, navigationName);
        }
    }

    private void AttachRelatedEntities(NavigationEntry navigation, ManyToManyInsertBehavior behavior)
    {
        foreach (var related in NavigationPropertyHelper.GetCollectionItems(navigation))
        {
            var relatedEntry = _context.Entry(related);
            if (relatedEntry.State != EntityState.Detached)
            {
                continue;
            }

            var state = DetermineRelatedEntityState(relatedEntry, behavior);
            relatedEntry.State = state;
        }
    }

    private EntityState DetermineRelatedEntityState(EntityEntry entry, ManyToManyInsertBehavior behavior)
    {
        if (behavior == ManyToManyInsertBehavior.AttachExisting)
        {
            return EntityState.Unchanged;
        }

        return HasDefaultKey(entry) ? EntityState.Added : EntityState.Unchanged;
    }

    private static bool HasDefaultKey(EntityEntry entry)
    {
        var keyProperty = entry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (keyProperty == null)
        {
            return false;
        }

        var keyValue = entry.Property(keyProperty.Name).CurrentValue;
        if (keyValue == null)
        {
            return true;
        }

        var defaultValue = keyProperty.ClrType.IsValueType
            ? Activator.CreateInstance(keyProperty.ClrType)
            : null;

        return keyValue.Equals(defaultValue);
    }

    private void ValidateRelatedEntitiesExist(EntityEntry parentEntry, NavigationEntry navigation)
    {
        var targetType = navigation.Metadata.TargetEntityType;
        var keyProperty = targetType.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (keyProperty == null)
        {
            return;
        }

        var relatedIds = GetRelatedEntityIds(navigation, keyProperty);
        if (relatedIds.Count == 0)
        {
            return;
        }

        var existingIds = QueryExistingIds(targetType.ClrType, keyProperty.Name, relatedIds);
        var missingIds = relatedIds.Except(existingIds).ToList();

        if (missingIds.Count > 0)
        {
            ThrowMissingRelatedEntityException(parentEntry, navigation, targetType.ClrType, missingIds);
        }
    }

    private static List<object> GetRelatedEntityIds(NavigationEntry navigation, IProperty keyProperty)
    {
        var ids = new List<object>();
        foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
        {
            var itemType = item.GetType();
            var prop = itemType.GetProperty(keyProperty.Name);
            var idValue = prop?.GetValue(item);
            if (idValue != null)
            {
                ids.Add(idValue);
            }
        }
        return ids;
    }

    private HashSet<object> QueryExistingIds(Type entityType, string keyPropertyName, List<object> ids)
    {
        var dbSet = _context.Model.FindEntityType(entityType);
        if (dbSet == null)
        {
            return [];
        }

        var existingIds = new HashSet<object>();
        var setMethod = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes);
        var genericSet = setMethod?.MakeGenericMethod(entityType);
        var queryable = genericSet?.Invoke(_context, null) as IQueryable<object>;

        if (queryable == null)
        {
            return existingIds;
        }

        var keyProp = entityType.GetProperty(keyPropertyName);
        foreach (var id in ids)
        {
            var exists = queryable.Any(e => keyProp!.GetValue(e)!.Equals(id));
            if (exists)
            {
                existingIds.Add(id);
            }
        }

        return existingIds;
    }

    private void ThrowMissingRelatedEntityException(
        EntityEntry parentEntry, NavigationEntry navigation, Type relatedType, List<object> missingIds)
    {
        var parentType = parentEntry.Metadata.ClrType.Name;
        var parentId = GetEntityIdSafe(parentEntry);
        var missingIdList = string.Join(", ", missingIds);

        throw new InvalidOperationException(
            $"Entity '{parentType}' (Id={parentId}) has many-to-many link to '{relatedType.Name}' " +
            $"via navigation '{navigation.Metadata.Name}', but the following related entities don't exist: [{missingIdList}]. " +
            $"Either insert the related entities first, or set ValidateManyToManyEntitiesExist=false to skip validation.");
    }

    private void RemoveJoinRecords(EntityEntry entry, ManyToManyStatisticsTracker tracker)
    {
        var entityTypeName = entry.Metadata.ClrType.Name;

        foreach (var navigation in ManyToManyNavigationHelper.GetManyToManyNavigations(entry))
        {
            var navigationName = navigation.Metadata.Name;
            var itemCount = CountNavigationItems(navigation);

            if (ManyToManyNavigationHelper.IsSkipNavigation(navigation))
            {
                ClearSkipNavigationLinks(navigation);
            }
            else
            {
                MarkExplicitJoinEntitiesAsDeleted(navigation);
            }

            for (var i = 0; i < itemCount; i++)
            {
                tracker.RecordJoinRemoved(entityTypeName, navigationName);
            }
        }
    }

    private static void ClearSkipNavigationLinks(NavigationEntry navigation)
    {
        if (navigation.CurrentValue is System.Collections.IList list)
        {
            list.Clear();
        }
    }

    private void MarkExplicitJoinEntitiesAsDeleted(NavigationEntry navigation)
    {
        foreach (var joinEntity in NavigationPropertyHelper.GetCollectionItems(navigation))
        {
            var joinEntry = _context.Entry(joinEntity);
            if (joinEntry.State != EntityState.Deleted)
            {
                joinEntry.State = EntityState.Deleted;
            }
        }
    }

    private static int CountNavigationItems(NavigationEntry navigation) =>
        navigation.CurrentValue is System.Collections.IEnumerable enumerable
            ? enumerable.Cast<object>().Count()
            : 0;

    private static object GetEntityIdSafe(EntityEntry entry)
    {
        var keyProperty = entry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (keyProperty == null)
        {
            return "unknown";
        }
        return entry.Property(keyProperty.Name).CurrentValue ?? "unknown";
    }
}
