using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EfCoreUtils.Internal.Services;

/// <summary>
/// Service for pre-validating many-to-many entity existence and caching results.
/// Performs batched queries to minimize database round trips.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Limitation:</strong> Many-to-many validation is currently not supported for entities
/// with composite primary keys. When a related entity has a composite key, validation is skipped
/// unless <see cref="InsertGraphBatchOptions.ThrowOnUnsupportedValidation"/> is set to true.
/// </para>
/// </remarks>
internal class ManyToManyValidationCache<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly DbContext _context;
    private readonly ManyToManyIdQueryService _queryService;
    private Dictionary<Type, HashSet<object>> _missingIdsByType = [];
    private bool _throwOnUnsupportedValidation;

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
        _throwOnUnsupportedValidation = options.ThrowOnUnsupportedValidation;

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
            var idValue = CompositeKeyHelper.ExtractEntityId(item, keyProperties);
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

        if (keyProperties.Count > 1)
        {
            return HandleCompositeKeyValidation(clrType, ids.Count);
        }

        var existingIds = _queryService.QueryExistingIds(clrType, keyProperties[0].Name, ids.ToList());
        return ids.Except(existingIds).ToHashSet();
    }

    private HashSet<object> HandleCompositeKeyValidation(Type clrType, int entityCount)
    {
        if (_throwOnUnsupportedValidation)
        {
            throw new NotSupportedException(
                $"Many-to-many validation for entities with composite keys is not yet supported. " +
                $"Entity type '{clrType.Name}' has a composite primary key and {entityCount} related entity(ies) " +
                $"could not be validated for existence. " +
                $"Set InsertGraphBatchOptions.ThrowOnUnsupportedValidation=false to skip validation silently, " +
                $"or set ValidateManyToManyEntitiesExist=false to disable validation entirely.");
        }

        return [];
    }

    private static IReadOnlyList<IProperty>? GetKeyPropertiesOrNull(IEntityType metadata) =>
        metadata.FindPrimaryKey()?.Properties;
}
