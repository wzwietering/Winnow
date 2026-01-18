using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EfCoreUtils.Internal.Services;

internal class ValidationService<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private const int AbsoluteMaxDepth = 100;

    private readonly DbContext _context;
    private readonly EntityKeyService<TEntity, TKey> _keyService;

    internal ValidationService(DbContext context, EntityKeyService<TEntity, TKey> keyService)
    {
        _context = context;
        _keyService = keyService;
    }

    internal void ValidateNoModifiedNavigationProperties(TEntity entity)
    {
        var entry = _context.Entry(entity);
        var modifiedNavigations = CollectModifiedNavigations(entry);

        if (modifiedNavigations.Count != 0)
        {
            var entityId = _keyService.GetEntityId(entity);
            throw new InvalidOperationException(
                $"Entity {typeof(TEntity).Name} (Id={entityId}) has modified navigation properties: " +
                $"{string.Join(", ", modifiedNavigations)}. " +
                $"BatchSaver<{typeof(TEntity).Name}, {typeof(TKey).Name}> only updates parent entities. " +
                $"To update entity graphs, use standard EF Core SaveChanges() or set " +
                $"BatchOptions.ValidateNavigationProperties = false to suppress this check.");
        }
    }

    private List<string> CollectModifiedNavigations(EntityEntry entry)
    {
        var modifiedNavigations = new List<string>();

        foreach (var navigation in entry.Navigations)
        {
            if (navigation.CurrentValue == null)
            {
                continue;
            }

            if (navigation.Metadata.IsCollection)
            {
                CheckCollectionNavigationForModifications(navigation, modifiedNavigations);
            }
            else
            {
                CheckReferenceNavigationForModifications(navigation, modifiedNavigations);
            }
        }

        return modifiedNavigations;
    }

    private void CheckCollectionNavigationForModifications(
        NavigationEntry navigation, List<string> modifiedNavigations)
    {
        if (navigation.CurrentValue is not System.Collections.IEnumerable collection)
        {
            return;
        }

        foreach (var item in collection)
        {
            var itemEntry = _context.Entry(item);
            if (itemEntry.State is EntityState.Added or EntityState.Deleted or EntityState.Modified)
            {
                modifiedNavigations.Add($"{navigation.Metadata.Name} (collection items)");
                break;
            }
        }
    }

    private void CheckReferenceNavigationForModifications(
        NavigationEntry navigation, List<string> modifiedNavigations)
    {
        var navEntry = _context.Entry(navigation.CurrentValue!);
        if (navEntry.State is EntityState.Modified or EntityState.Added or EntityState.Deleted)
        {
            modifiedNavigations.Add(navigation.Metadata.Name);
        }
    }

    internal void ValidateNoPopulatedNavigationProperties(TEntity entity)
    {
        var entry = _context.Entry(entity);
        var populatedNavigations = CollectPopulatedNavigations(entry);

        if (populatedNavigations.Count != 0)
        {
            throw new InvalidOperationException(
                $"Entity {typeof(TEntity).Name} has populated navigation properties: " +
                $"{string.Join(", ", populatedNavigations)}. " +
                $"Use InsertGraphBatch to insert parent with children, or clear the navigations.");
        }
    }

    internal void ValidateNoPopulatedNavigationPropertiesForDelete(TEntity entity)
    {
        var entry = _context.Entry(entity);
        var populatedNavigations = CollectPopulatedNavigations(entry);

        if (populatedNavigations.Count != 0)
        {
            var entityId = _keyService.GetEntityId(entity);
            throw new InvalidOperationException(
                $"Entity {typeof(TEntity).Name} (Id={entityId}) has populated navigation properties: " +
                $"{string.Join(", ", populatedNavigations)}. " +
                $"Use DeleteGraphBatch to delete parent with children, or remove Include().");
        }
    }

    private List<string> CollectPopulatedNavigations(EntityEntry entry)
    {
        var populatedNavigations = new List<string>();

        foreach (var navigation in entry.Navigations)
        {
            if (navigation.CurrentValue == null)
            {
                continue;
            }

            if (navigation.Metadata.IsCollection)
            {
                if (navigation.CurrentValue is System.Collections.IEnumerable collection &&
                    collection.Cast<object>().Any())
                {
                    populatedNavigations.Add($"{navigation.Metadata.Name} (collection)");
                }
            }
            else
            {
                populatedNavigations.Add(navigation.Metadata.Name);
            }
        }

        return populatedNavigations;
    }

    internal void ValidateCascadeBehavior(TEntity entity, DeleteGraphBatchOptions options)
    {
        if (options.CascadeBehavior != DeleteCascadeBehavior.Throw)
        {
            return;
        }

        var entry = _context.Entry(entity);
        var entityId = _keyService.GetEntityId(entity);
        ValidateEntityHasNoChildren(entry, entityId);
    }

    internal void ValidateCascadeBehaviorRecursive(
        TEntity entity, int maxDepth, DeleteGraphBatchOptions options)
    {
        if (options.CascadeBehavior != DeleteCascadeBehavior.Throw)
        {
            return;
        }

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        ValidateCascadeRecursive(entity, 0, ClampDepth(maxDepth), visited);
    }

    private void ValidateCascadeRecursive(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited)
    {
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);
        ValidateEntityHasNoChildrenWithDepth(entry, currentDepth);

        if (currentDepth >= maxDepth)
        {
            return;
        }

        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                ValidateCascadeRecursive(item, currentDepth + 1, maxDepth, visited);
            }
        }
    }

    private void ValidateEntityHasNoChildren(EntityEntry entry, TKey entityId)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (navigation.CurrentValue == null || !navigation.Metadata.IsCollection)
            {
                continue;
            }

            if (navigation.CurrentValue is System.Collections.IEnumerable collection)
            {
                var childCount = collection.Cast<object>().Count();
                if (childCount > 0)
                {
                    throw new InvalidOperationException(
                        $"Entity {typeof(TEntity).Name} (Id={entityId}) has {childCount} child(ren) in " +
                        $"'{navigation.Metadata.Name}'. " +
                        $"Set DeleteGraphBatchOptions.CascadeBehavior to Cascade or ParentOnly to proceed.");
                }
            }
        }
    }

    private void ValidateEntityHasNoChildrenWithDepth(EntityEntry entry, int depth)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (navigation.CurrentValue == null || !navigation.Metadata.IsCollection)
            {
                continue;
            }

            if (navigation.CurrentValue is System.Collections.IEnumerable collection)
            {
                var childCount = collection.Cast<object>().Count();
                if (childCount > 0)
                {
                    var entityId = _keyService.GetEntityIdFromEntry(entry);
                    throw new InvalidOperationException(
                        $"Entity {entry.Metadata.ClrType.Name} (Id={entityId}) at depth {depth} has " +
                        $"{childCount} child(ren) in '{navigation.Metadata.Name}'. " +
                        $"Set DeleteGraphBatchOptions.CascadeBehavior to Cascade or ParentOnly to proceed.");
                }
            }
        }
    }

    private static int ClampDepth(int maxDepth) => Math.Min(maxDepth, AbsoluteMaxDepth);

    // ========== Reference Validation Methods ==========

    internal void ValidateCircularReferences(TEntity entity, int maxDepth)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        ValidateCircularReferencesRecursive(entity, 0, ClampDepth(maxDepth), visited);
    }

    private void ValidateCircularReferencesRecursive(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited)
    {
        if (!visited.Add(entity))
        {
            var entry = _context.Entry(entity);
            var entityType = entry.Metadata.ClrType.Name;
            var entityId = _keyService.GetEntityIdFromEntry(entry);
            throw new InvalidOperationException(
                $"Circular reference detected: Entity '{entityType}' (Id={entityId}) at depth {currentDepth} " +
                $"was already visited. Set CircularReferenceHandling to Ignore to process each entity once.");
        }

        if (currentDepth >= maxDepth)
        {
            return;
        }

        var entry2 = _context.Entry(entity);
        ValidateCollectionReferencesRecursive(entry2, currentDepth, maxDepth, visited);
        ValidateReferenceNavigationsRecursive(entry2, currentDepth, maxDepth, visited);
    }

    private void ValidateCollectionReferencesRecursive(
        EntityEntry entry, int currentDepth, int maxDepth, HashSet<object> visited)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                ValidateCircularReferencesRecursive(item, currentDepth + 1, maxDepth, visited);
            }
        }
    }

    private void ValidateReferenceNavigationsRecursive(
        EntityEntry entry, int currentDepth, int maxDepth, HashSet<object> visited)
    {
        foreach (var navigation in NavigationPropertyHelper.GetReferenceNavigations(entry))
        {
            var refEntity = NavigationPropertyHelper.GetReferenceValue(navigation);
            if (refEntity == null)
            {
                continue;
            }

            ValidateSelfReference(entry, navigation, refEntity);
            ValidateCircularReferencesRecursive(refEntity, currentDepth + 1, maxDepth, visited);
        }
    }

    private void ValidateSelfReference(EntityEntry entry, NavigationEntry navigation, object refEntity)
    {
        if (ReferenceEquals(entry.Entity, refEntity))
        {
            var entityType = entry.Metadata.ClrType.Name;
            var entityId = _keyService.GetEntityIdFromEntry(entry);
            throw new InvalidOperationException(
                $"Entity '{entityType}' (Id={entityId}) references itself via navigation '{navigation.Metadata.Name}'. " +
                $"Self-referential entities are not supported in graph batch operations.");
        }
    }

    internal void ValidateReferencedEntitiesExist(TEntity entity, int maxDepth)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        ValidateReferencedEntitiesExistRecursive(entity, 0, ClampDepth(maxDepth), visited);
    }

    private void ValidateReferencedEntitiesExistRecursive(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited)
    {
        if (!visited.Add(entity))
        {
            return;
        }

        if (currentDepth >= maxDepth)
        {
            return;
        }

        var entry = _context.Entry(entity);
        ValidateEntityReferencesExist(entry);

        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                ValidateReferencedEntitiesExistRecursive(item, currentDepth + 1, maxDepth, visited);
            }
        }
    }

    private void ValidateEntityReferencesExist(EntityEntry entry)
    {
        foreach (var navigation in NavigationPropertyHelper.GetReferenceNavigations(entry))
        {
            var refEntity = NavigationPropertyHelper.GetReferenceValue(navigation);
            if (refEntity == null)
            {
                continue;
            }

            var refEntry = _context.Entry(refEntity);
            if (HasDefaultKeyValue(refEntry))
            {
                var entityType = entry.Metadata.ClrType.Name;
                var entityId = _keyService.GetEntityIdFromEntry(entry);
                var refType = refEntry.Metadata.ClrType.Name;

                throw new InvalidOperationException(
                    $"Entity '{entityType}' (Id={entityId}) references '{refType}' " +
                    $"via navigation '{navigation.Metadata.Name}', but the referenced entity has a default key " +
                    $"value and likely does not exist in the database. This would cause an FK constraint violation. " +
                    $"Set ValidateReferencedEntitiesExist to false to skip this validation.");
            }
        }
    }

    private static bool HasDefaultKeyValue(EntityEntry entry)
    {
        var keyProperties = entry.Metadata.FindPrimaryKey()?.Properties;
        if (keyProperties == null)
        {
            return false;
        }

        foreach (var keyProperty in keyProperties)
        {
            var value = entry.Property(keyProperty.Name).CurrentValue;
            if (!IsDefaultValue(value, keyProperty.ClrType))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsDefaultValue(object? value, Type type)
    {
        if (value == null)
        {
            return true;
        }

        if (type == typeof(int) || type == typeof(int?))
        {
            return value is int i && i == 0;
        }
        if (type == typeof(long) || type == typeof(long?))
        {
            return value is long l && l == 0;
        }
        if (type == typeof(Guid) || type == typeof(Guid?))
        {
            return value is Guid g && g == Guid.Empty;
        }
        if (type == typeof(string))
        {
            return value is string s && string.IsNullOrEmpty(s);
        }

        return false;
    }
}
