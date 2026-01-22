using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EfCoreUtils.Internal.Services;

/// <summary>
/// Service for managing many-to-many join records during graph operations.
/// Handles both skip navigations (EF-managed) and explicit join entities.
/// </summary>
/// <remarks>
/// <para>
/// This service modifies the DbContext's change tracker but does not call SaveChanges.
/// The caller is responsible for wrapping operations in a transaction when atomicity
/// is required across multiple entities.
/// </para>
/// <para>
/// For batched validation (<see cref="ValidateManyToManyEntitiesExistBatched"/>),
/// database queries are executed immediately to check for missing entities.
/// These queries use AsNoTracking and do not affect the change tracker.
/// </para>
/// </remarks>
internal class ManyToManyLinkService<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private static readonly System.Reflection.MethodInfo QueryExistingIdsGenericMethod =
        typeof(ManyToManyLinkService<TEntity, TKey>)
            .GetMethod(nameof(QueryExistingIdsGeneric),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
        ?? throw new InvalidOperationException("QueryExistingIdsGeneric method not found");

    private static readonly ConcurrentDictionary<(Type EntityType, Type KeyType), System.Reflection.MethodInfo>
        GenericMethodCache = new();

    private readonly DbContext _context;
    private readonly EntityKeyService<TEntity, TKey> _keyService;

    // Cache of missing IDs by entity type, populated during batched validation
    private Dictionary<Type, HashSet<object>> _missingIdsByType = [];

    internal ManyToManyLinkService(DbContext context, EntityKeyService<TEntity, TKey> keyService)
    {
        _context = context;
        _keyService = keyService;
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

    internal ManyToManyStatisticsTracker ProcessManyToManyForDelete(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var tracker = new ManyToManyStatisticsTracker();
        var entry = _context.Entry(entity);

        RemoveJoinRecords(entry, tracker);
        return tracker;
    }

    /// <summary>
    /// Pre-validates that all referenced M2M entities exist by doing batched queries.
    /// Caches missing IDs for per-entity validation during ProcessManyToManyForInsert.
    /// Does not throw - failures are recorded per-entity when processing.
    /// </summary>
    internal void ValidateManyToManyEntitiesExistBatched(
        List<TEntity> entities, InsertGraphBatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(options);

        _missingIdsByType.Clear();

        if (!options.ValidateManyToManyEntitiesExist ||
            options.ManyToManyInsertBehavior != ManyToManyInsertBehavior.AttachExisting)
        {
            return;
        }

        var idsByTargetType = CollectAllRelatedIds(entities, options.MaxDepth);
        _missingIdsByType = FindMissingIds(idsByTargetType);
    }

    private Dictionary<Type, (IEntityType Metadata, HashSet<object> Ids)> CollectAllRelatedIds(
        List<TEntity> entities, int maxDepth)
    {
        var idsByTargetType = new Dictionary<Type, (IEntityType Metadata, HashSet<object> Ids)>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach (var entity in entities)
        {
            CollectEntityRelatedIds(entity, idsByTargetType, visited, 0, maxDepth);
        }

        return idsByTargetType;
    }

    private void CollectEntityRelatedIds(
        object entity, Dictionary<Type, (IEntityType Metadata, HashSet<object> Ids)> idsByTargetType,
        HashSet<object> visited, int depth, int maxDepth)
    {
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);
        CollectEntryRelatedIds(entry, idsByTargetType);

        if (depth >= maxDepth)
        {
            return;
        }

        CollectChildRelatedIds(entry, idsByTargetType, visited, depth, maxDepth);
    }

    private static void CollectEntryRelatedIds(
        EntityEntry entry, Dictionary<Type, (IEntityType Metadata, HashSet<object> Ids)> idsByTargetType)
    {
        foreach (var navigation in ManyToManyNavigationHelper.GetManyToManyNavigations(entry))
        {
            CollectNavigationRelatedIds(navigation, idsByTargetType);
        }
    }

    private static void CollectNavigationRelatedIds(
        NavigationEntry navigation, Dictionary<Type, (IEntityType Metadata, HashSet<object> Ids)> idsByTargetType)
    {
        var targetType = navigation.Metadata.TargetEntityType;
        var keyProperty = targetType.FindPrimaryKey()?.Properties.FirstOrDefault();
        if (keyProperty == null)
        {
            return;
        }

        var clrType = targetType.ClrType;
        var idSet = GetOrCreateIdSet(idsByTargetType, clrType, targetType);

        foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
        {
            var idValue = ExtractEntityId(item, keyProperty.Name);
            if (idValue != null)
            {
                idSet.Add(idValue);
            }
        }
    }

    private static HashSet<object> GetOrCreateIdSet(
        Dictionary<Type, (IEntityType Metadata, HashSet<object> Ids)> idsByTargetType,
        Type clrType, IEntityType metadata)
    {
        if (!idsByTargetType.TryGetValue(clrType, out var existing))
        {
            existing = (metadata, []);
            idsByTargetType[clrType] = existing;
        }
        return existing.Ids;
    }

    private static object? ExtractEntityId(object item, string keyPropertyName)
    {
        var itemType = item.GetType();
        var prop = itemType.GetProperty(keyPropertyName);
        return prop?.GetValue(item);
    }

    private void CollectChildRelatedIds(
        EntityEntry entry, Dictionary<Type, (IEntityType Metadata, HashSet<object> Ids)> idsByTargetType,
        HashSet<object> visited, int depth, int maxDepth)
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
                CollectEntityRelatedIds(child, idsByTargetType, visited, depth + 1, maxDepth);
            }
        }
    }

    private Dictionary<Type, HashSet<object>> FindMissingIds(
        Dictionary<Type, (IEntityType Metadata, HashSet<object> Ids)> idsByTargetType)
    {
        var missingByType = new Dictionary<Type, HashSet<object>>();

        foreach (var (clrType, (metadata, ids)) in idsByTargetType)
        {
            if (ids.Count == 0)
            {
                continue;
            }

            var keyProperty = metadata.FindPrimaryKey()?.Properties.FirstOrDefault();
            if (keyProperty == null)
            {
                continue;
            }

            var existingIds = QueryExistingIds(clrType, keyProperty.Name, ids.ToList());
            var missingIds = ids.Except(existingIds).ToHashSet();

            if (missingIds.Count > 0)
            {
                missingByType[clrType] = missingIds;
            }
        }

        return missingByType;
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

        ManyToManyValidation.ValidateCollectionSize(entityTypeName, navigationName, itemCount, options.MaxManyToManyCollectionSize);

        // Check against cached missing IDs (populated by batched validation)
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

        var missingIds = GetCachedMissingIds(clrType);
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

    private HashSet<object>? GetCachedMissingIds(Type clrType)
    {
        if (_missingIdsByType.TryGetValue(clrType, out var missingIds) && missingIds.Count > 0)
        {
            return missingIds;
        }
        return null;
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

        // Don't override Modified or Deleted states - those indicate intentional changes
        if (currentState == EntityState.Modified || currentState == EntityState.Deleted)
        {
            return false;
        }

        return true;
    }

    private EntityState DetermineRelatedEntityState(EntityEntry entry, ManyToManyInsertBehavior behavior)
    {
        if (behavior == ManyToManyInsertBehavior.AttachExisting)
        {
            return EntityState.Unchanged;
        }

        // InsertIfNew: insert entities with temporary keys, attach others
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

    private HashSet<object> QueryExistingIds(Type entityType, string keyPropertyName, List<object> ids)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var entityMetadata = _context.Model.FindEntityType(entityType);
        if (entityMetadata == null)
        {
            return [];
        }

        var keyProp = entityType.GetProperty(keyPropertyName)
            ?? throw new InvalidOperationException(
                $"Property '{keyPropertyName}' not found on type {entityType.Name}");

        var genericMethod = GenericMethodCache.GetOrAdd(
            (entityType, keyProp.PropertyType),
            key => QueryExistingIdsGenericMethod.MakeGenericMethod(key.EntityType, key.KeyType));

        var result = genericMethod.Invoke(this, [keyPropertyName, ids]);
        if (result is not HashSet<object> hashSet)
        {
            throw new InvalidOperationException(
                $"Failed to query existing IDs for type {entityType.Name}. " +
                "Expected HashSet<object> but got null or incompatible type.");
        }
        return hashSet;
    }

    private HashSet<object> QueryExistingIdsGeneric<TEntityType, TKeyType>(string keyPropertyName, List<object> ids)
        where TEntityType : class
    {
        var typedIds = ConvertIds<TKeyType>(ids);

        // Use AsNoTracking to query only the database, not the local change tracker
        var existingIds = _context.Set<TEntityType>()
            .AsNoTracking()
            .Where(e => typedIds.Contains(EF.Property<TKeyType>(e, keyPropertyName)))
            .Select(e => EF.Property<TKeyType>(e, keyPropertyName))
            .ToList();

        return existingIds.Cast<object>().ToHashSet();
    }

    private static List<TKeyType> ConvertIds<TKeyType>(List<object> ids)
    {
        var typedIds = new List<TKeyType>(ids.Count);
        foreach (var id in ids)
        {
            try
            {
                var targetType = Nullable.GetUnderlyingType(typeof(TKeyType)) ?? typeof(TKeyType);
                var converted = (TKeyType)Convert.ChangeType(id, targetType);
                typedIds.Add(converted);
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
            {
                throw new InvalidOperationException(
                    $"Cannot convert ID value '{id}' of type {id.GetType().Name} to {typeof(TKeyType).Name}.", ex);
            }
        }
        return typedIds;
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
}
