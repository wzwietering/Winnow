using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EfCoreUtils.Internal.Services;

/// <summary>
/// Service for handling many-to-many relationship setup during insert operations.
/// </summary>
internal class ManyToManyInsertProcessor<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly DbContext _context;
    private readonly ManyToManyValidationCache<TEntity, TKey> _validationCache;

    internal ManyToManyInsertProcessor(
        DbContext context,
        ManyToManyValidationCache<TEntity, TKey> validationCache)
    {
        _context = context;
        _validationCache = validationCache;
    }

    internal ManyToManyStatisticsTracker ProcessManyToManyForInsert(
        TEntity entity, InsertGraphBatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxDepth < 0 || options.MaxDepth > DepthConstants.AbsoluteMaxDepth)
        {
            throw new ArgumentOutOfRangeException(nameof(options),
                $"MaxDepth must be between 0 and {DepthConstants.AbsoluteMaxDepth}");
        }

        var tracker = new ManyToManyStatisticsTracker();
        var entry = _context.Entry(entity);
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { entity };

        ProcessEntityManyToMany(entry, options, tracker, visited, 0, options.MaxDepth);
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
        var itemCount = CountNavigationItems(navigation);

        ManyToManyValidation.ValidateCollectionSize(
            entityTypeName, navigationName, itemCount, options.MaxManyToManyCollectionSize);

        ValidateAgainstCachedMissingIds(entry, navigation);

        AttachRelatedEntities(navigation, options.ManyToManyInsertBehavior);

        for (var i = 0; i < itemCount; i++)
        {
            tracker.RecordJoinCreated(entityTypeName, navigationName);
        }
    }

    private void ValidateAgainstCachedMissingIds(EntityEntry parentEntry, NavigationEntry navigation)
    {
        var targetType = navigation.Metadata.TargetEntityType;
        var clrType = targetType.ClrType;

        var missingIds = _validationCache.GetCachedMissingIds(clrType);
        if (missingIds == null)
        {
            return;
        }

        var keyProperty = targetType.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (keyProperty == null)
        {
            return;
        }

        var entityMissingIds = CollectEntityMissingIds(navigation, keyProperty.Name, missingIds);
        ThrowIfMissingIds(parentEntry, navigation, clrType, entityMissingIds);
    }

    private static List<object> CollectEntityMissingIds(
        NavigationEntry navigation, string keyPropertyName, HashSet<object> missingIds)
    {
        var entityMissingIds = new List<object>();
        foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
        {
            var idValue = ExtractEntityId(item, keyPropertyName);
            if (idValue != null && missingIds.Contains(idValue))
            {
                entityMissingIds.Add(idValue);
            }
        }
        return entityMissingIds;
    }

    private static void ThrowIfMissingIds(
        EntityEntry parentEntry, NavigationEntry navigation, Type clrType, List<object> entityMissingIds)
    {
        if (entityMissingIds.Count == 0)
        {
            return;
        }

        var parentType = parentEntry.Metadata.ClrType.Name;
        var parentId = EntityEntryHelper.GetEntityIdSafe(parentEntry);
        var missingIdList = string.Join(", ", entityMissingIds);

        throw new InvalidOperationException(
            $"Entity '{parentType}' (Id={parentId}) has many-to-many link to '{clrType.Name}' " +
            $"via navigation '{navigation.Metadata.Name}', but the following related entities don't exist: [{missingIdList}]. " +
            $"Either insert the related entities first, or set ValidateManyToManyEntitiesExist=false to skip validation.");
    }

    private void AttachRelatedEntities(NavigationEntry navigation, ManyToManyInsertBehavior behavior)
    {
        foreach (var related in NavigationPropertyHelper.GetCollectionItems(navigation))
        {
            var relatedEntry = _context.Entry(related);
            var desiredState = DetermineRelatedEntityState(relatedEntry, behavior);

            if (ShouldChangeEntityState(relatedEntry.State, desiredState))
            {
                relatedEntry.State = desiredState;
            }
        }
    }

    private static bool ShouldChangeEntityState(EntityState currentState, EntityState desiredState)
    {
        if (currentState == desiredState)
        {
            return false;
        }

        if (currentState == EntityState.Modified || currentState == EntityState.Deleted)
        {
            return false;
        }

        return true;
    }

    private static EntityState DetermineRelatedEntityState(EntityEntry entry, ManyToManyInsertBehavior behavior)
    {
        if (behavior == ManyToManyInsertBehavior.AttachExisting)
        {
            return EntityState.Unchanged;
        }

        if (entry.State == EntityState.Added && HasTemporaryKey(entry))
        {
            return EntityState.Added;
        }

        return HasDefaultKey(entry) ? EntityState.Added : EntityState.Unchanged;
    }

    private static bool HasTemporaryKey(EntityEntry entry)
    {
        var keyProperty = entry.Metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (keyProperty == null)
        {
            return false;
        }

        return entry.Property(keyProperty.Name).IsTemporary;
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

    private static object? ExtractEntityId(object item, string keyPropertyName)
    {
        var itemType = item.GetType();
        var prop = itemType.GetProperty(keyPropertyName);
        return prop?.GetValue(item);
    }

    private static int CountNavigationItems(NavigationEntry navigation) =>
        navigation.CurrentValue is System.Collections.IEnumerable enumerable
            ? enumerable.Cast<object>().Count()
            : 0;
}
