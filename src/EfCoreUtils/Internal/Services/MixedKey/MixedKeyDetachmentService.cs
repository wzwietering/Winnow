using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EfCoreUtils.Internal.Services.MixedKey;

/// <summary>
/// Non-generic detachment service for entities with mixed key types.
/// </summary>
internal class MixedKeyDetachmentService
{
    private const int AbsoluteMaxDepth = 100;

    private readonly DbContext _context;

    internal MixedKeyDetachmentService(DbContext context) => _context = context;

    internal void DetachEntity(object entity)
    {
        var entry = _context.Entry(entity);
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Detached;
        }
    }

    internal void DetachEntityGraph(object entity)
    {
        var entry = _context.Entry(entity);

        foreach (var navigation in entry.Navigations)
        {
            if (navigation.CurrentValue == null)
            {
                continue;
            }

            if (navigation.Metadata.IsCollection)
            {
                DetachCollectionItems(navigation);
            }
            else
            {
                DetachReferenceNavigation(navigation);
            }
        }

        DetachEntity(entity);
    }

    private void DetachCollectionItems(NavigationEntry navigation)
    {
        var items = NavigationPropertyHelper.GetCollectionItems(navigation).ToList();
        foreach (var item in items)
        {
            var itemEntry = _context.Entry(item);
            if (itemEntry.State != EntityState.Detached)
            {
                itemEntry.State = EntityState.Detached;
            }
        }
    }

    private void DetachReferenceNavigation(NavigationEntry navigation)
    {
        var navEntry = _context.Entry(navigation.CurrentValue!);
        if (navEntry.State != EntityState.Detached)
        {
            navEntry.State = EntityState.Detached;
        }
    }

    internal void DetachAllEntities<TEntity>(
        List<TEntity> entities,
        Dictionary<(string Type, object Id), List<object>> deletedChildrenByParentRecursive)
        where TEntity : class
    {
        foreach (var deletedChildren in deletedChildrenByParentRecursive.Values)
        {
            DetachObjects(deletedChildren);
        }

        foreach (var entity in entities)
        {
            DetachEntityGraph(entity);
        }
    }

    private void DetachObjects(List<object> objects)
    {
        foreach (var obj in objects)
        {
            var entry = _context.Entry(obj);
            if (entry.State != EntityState.Detached)
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    // ========== Recursive Detachment Methods ==========

    internal void DetachEntityGraphRecursive(object entity, int maxDepth)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        DetachRecursive(entity, 0, ClampDepth(maxDepth), visited);
    }

    private void DetachRecursive(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited)
    {
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);

        if (currentDepth < maxDepth)
        {
            DetachChildrenRecursive(entry, currentDepth, maxDepth, visited);
        }

        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Detached;
        }
    }

    private void DetachChildrenRecursive(
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
                DetachRecursive(item, currentDepth + 1, maxDepth, visited);
            }
        }
    }

    private static int ClampDepth(int maxDepth) => Math.Min(maxDepth, AbsoluteMaxDepth);
}
