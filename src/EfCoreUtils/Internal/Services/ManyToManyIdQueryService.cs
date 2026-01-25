using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Internal.Services;

/// <summary>
/// Service for querying existing entity IDs from the database.
/// Used to validate many-to-many relationships reference existing entities.
/// </summary>
internal class ManyToManyIdQueryService
{
    private static readonly System.Reflection.MethodInfo QueryExistingIdsGenericMethod =
        typeof(ManyToManyIdQueryService)
            .GetMethod(nameof(QueryExistingIdsGeneric),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
        ?? throw new InvalidOperationException("QueryExistingIdsGeneric method not found");

    private static readonly ConcurrentDictionary<(Type EntityType, Type KeyType), System.Reflection.MethodInfo>
        GenericMethodCache = new();

    private readonly DbContext _context;

    internal ManyToManyIdQueryService(DbContext context)
    {
        _context = context;
    }

    internal HashSet<object> QueryExistingIds(Type entityType, string keyPropertyName, List<object> ids)
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
        return result as HashSet<object>
            ?? throw new InvalidOperationException(
                $"Failed to query existing IDs for type {entityType.Name}.");
    }

    private HashSet<object> QueryExistingIdsGeneric<TEntityType, TKeyType>(string keyPropertyName, List<object> ids)
        where TEntityType : class
    {
        var typedIds = ConvertIds<TKeyType>(ids);

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
}
