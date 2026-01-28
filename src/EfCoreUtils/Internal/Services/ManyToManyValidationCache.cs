using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EfCoreUtils.Internal.Services;

/// <summary>
/// Service for pre-validating many-to-many entity existence and caching results.
/// Performs batched queries to minimize database round trips.
/// </summary>
internal class ManyToManyValidationCache<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly DbContext _context;
    private readonly ManyToManyIdQueryService _queryService;
    private Dictionary<Type, HashSet<object>> _missingIdsByType = [];

    internal ManyToManyValidationCache(DbContext context, ManyToManyIdQueryService queryService)
    {
        _context = context;
        _queryService = queryService;
    }

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

    internal HashSet<object>? GetCachedMissingIds(Type clrType)
    {
        if (_missingIdsByType.TryGetValue(clrType, out var missingIds) && missingIds.Count > 0)
        {
            return missingIds;
        }
        return null;
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
        var keyProperties = targetType.FindPrimaryKey()?.Properties;
        if (keyProperties == null || keyProperties.Count == 0)
        {
            return;
        }

        var clrType = targetType.ClrType;
        var idSet = GetOrCreateIdSet(idsByTargetType, clrType, targetType);

        foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
        {
            var idValue = ExtractEntityId(item, keyProperties);
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

    private static object? ExtractEntityId(object item, IReadOnlyList<IProperty> keyProperties)
    {
        var itemType = item.GetType();

        if (keyProperties.Count == 1)
        {
            var prop = itemType.GetProperty(keyProperties[0].Name);
            return prop?.GetValue(item);
        }

        var values = new object[keyProperties.Count];
        for (var i = 0; i < keyProperties.Count; i++)
        {
            var prop = itemType.GetProperty(keyProperties[i].Name);
            var value = prop?.GetValue(item);
            if (value == null)
            {
                return null;
            }

            values[i] = value;
        }
        return new CompositeKey(values);
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
            var missingIds = FindMissingIdsForType(clrType, metadata, ids);
            if (missingIds.Count > 0)
            {
                missingByType[clrType] = missingIds;
            }
        }

        return missingByType;
    }

    private HashSet<object> FindMissingIdsForType(Type clrType, IEntityType metadata, HashSet<object> ids)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var keyProperties = GetKeyPropertiesOrNull(metadata);
        if (keyProperties == null || keyProperties.Count == 0)
        {
            return [];
        }

        // For composite keys, skip database validation for now
        // The query service currently only supports single-column key queries
        if (keyProperties.Count > 1)
        {
            return [];
        }

        var existingIds = _queryService.QueryExistingIds(clrType, keyProperties[0].Name, ids.ToList());
        return ids.Except(existingIds).ToHashSet();
    }

    private static IReadOnlyList<IProperty>? GetKeyPropertiesOrNull(IEntityType metadata) =>
        metadata.FindPrimaryKey()?.Properties;
}
