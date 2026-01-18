using EfCoreUtils.MixedKey;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EfCoreUtils.Internal.Services.MixedKey;

/// <summary>
/// Non-generic validation service for entities with mixed key types.
/// </summary>
internal class MixedKeyValidationService<TEntity>
    where TEntity : class
{
    private const int AbsoluteMaxDepth = 100;

    private readonly DbContext _context;
    private readonly MixedKeyEntityKeyService _keyService;

    internal MixedKeyValidationService(DbContext context, MixedKeyEntityKeyService keyService)
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
            var entityId = _keyService.GetEntityKey(entity);
            throw new InvalidOperationException(
                $"Entity {typeof(TEntity).Name} (Id={entityId}) has modified navigation properties: " +
                $"{string.Join(", ", modifiedNavigations)}. " +
                $"BatchSaver only updates parent entities. " +
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
        foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
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
            var entityId = _keyService.GetEntityKey(entity);
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
                var items = NavigationPropertyHelper.GetCollectionItems(navigation);
                if (items.Any())
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
        var entityId = _keyService.GetEntityKey(entity);
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

    private void ValidateEntityHasNoChildren(EntityEntry entry, MixedKeyId entityId)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            var childCount = NavigationPropertyHelper.GetCollectionItems(navigation).Count();
            if (childCount > 0)
            {
                throw new InvalidOperationException(
                    $"Entity {typeof(TEntity).Name} (Id={entityId}) has {childCount} child(ren) in " +
                    $"'{navigation.Metadata.Name}'. " +
                    $"Set DeleteGraphBatchOptions.CascadeBehavior to Cascade or ParentOnly to proceed.");
            }
        }
    }

    private void ValidateEntityHasNoChildrenWithDepth(EntityEntry entry, int depth)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            var childCount = NavigationPropertyHelper.GetCollectionItems(navigation).Count();
            if (childCount > 0)
            {
                var entityId = _keyService.GetEntityKey(entry);
                throw new InvalidOperationException(
                    $"Entity {entry.Metadata.ClrType.Name} (Id={entityId}) at depth {depth} has " +
                    $"{childCount} child(ren) in '{navigation.Metadata.Name}'. " +
                    $"Set DeleteGraphBatchOptions.CascadeBehavior to Cascade or ParentOnly to proceed.");
            }
        }
    }

    private static int ClampDepth(int maxDepth) => Math.Min(maxDepth, AbsoluteMaxDepth);
}
