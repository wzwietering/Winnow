using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EfCoreUtils.Internal.Services.MixedKey;

/// <summary>
/// Non-generic attachment service for entities with mixed key types.
/// </summary>
internal class MixedKeyAttachmentService
{
    private const int AbsoluteMaxDepth = 100;

    private readonly DbContext _context;

    internal MixedKeyAttachmentService(DbContext context) => _context = context;

    internal void AttachEntityAsDeleted(object entity) =>
        _context.Entry(entity).State = EntityState.Deleted;

    internal void AttachEntityGraphAsAdded(object entity)
    {
        var entry = _context.Entry(entity);
        entry.State = EntityState.Added;

        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            AttachCollectionChildrenAsState(navigation, EntityState.Added);
        }
    }

    internal void AttachEntityGraphAsModified(object entity)
    {
        var entry = _context.Entry(entity);
        entry.State = EntityState.Modified;

        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            AttachCollectionChildrenAsState(navigation, EntityState.Modified);
        }
    }

    internal void AttachEntityGraphAsDeleted(object entity)
    {
        var entry = _context.Entry(entity);

        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            MarkCollectionChildrenAsDeleted(navigation);
        }

        entry.State = EntityState.Deleted;
    }

    private void AttachCollectionChildrenAsState(NavigationEntry navigation, EntityState state)
    {
        foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
        {
            var itemEntry = _context.Entry(item);
            if (itemEntry.State == EntityState.Detached)
            {
                itemEntry.State = state;
            }
        }
    }

    private void MarkCollectionChildrenAsDeleted(NavigationEntry navigation)
    {
        var items = NavigationPropertyHelper.GetCollectionItems(navigation).ToList();
        foreach (var item in items)
        {
            _context.Entry(item).State = EntityState.Deleted;
        }
    }

    // ========== Recursive Attachment Methods ==========

    internal void AttachEntityGraphAsAddedRecursive(object entity, int maxDepth)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        AttachAsAddedRecursive(entity, 0, ClampDepth(maxDepth), visited);
    }

    private void AttachAsAddedRecursive(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited)
    {
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);
        entry.State = EntityState.Added;

        if (currentDepth >= maxDepth)
        {
            return;
        }

        AttachChildrenRecursive(entry, currentDepth, maxDepth, visited, EntityState.Added);
    }

    internal void AttachEntityGraphAsModifiedRecursive(object entity, int maxDepth)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        AttachAsModifiedRecursive(entity, 0, ClampDepth(maxDepth), visited);
    }

    private void AttachAsModifiedRecursive(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited)
    {
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);
        if (entry.State == EntityState.Detached)
        {
            entry.State = EntityState.Modified;
        }

        if (currentDepth >= maxDepth)
        {
            return;
        }

        AttachChildrenRecursive(entry, currentDepth, maxDepth, visited, EntityState.Modified);
    }

    private void AttachChildrenRecursive(
        EntityEntry entry, int currentDepth, int maxDepth,
        HashSet<object> visited, EntityState targetState)
    {
        foreach (var navigation in entry.Navigations)
        {
            if (!NavigationPropertyHelper.IsTraversableCollection(navigation))
            {
                continue;
            }

            foreach (var item in NavigationPropertyHelper.GetCollectionItems(navigation))
            {
                if (targetState == EntityState.Added)
                {
                    AttachAsAddedRecursive(item, currentDepth + 1, maxDepth, visited);
                }
                else
                {
                    AttachAsModifiedRecursive(item, currentDepth + 1, maxDepth, visited);
                }
            }
        }
    }

    // CRITICAL: Delete uses depth-first order - children before parent for FK constraints
    internal void AttachEntityGraphAsDeletedRecursive(object entity, int maxDepth)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        AttachAsDeletedRecursive(entity, 0, ClampDepth(maxDepth), visited);
    }

    private void AttachAsDeletedRecursive(
        object entity, int currentDepth, int maxDepth, HashSet<object> visited)
    {
        if (!visited.Add(entity))
        {
            return;
        }

        var entry = _context.Entry(entity);

        if (currentDepth < maxDepth)
        {
            DeleteChildrenRecursive(entry, currentDepth, maxDepth, visited);
        }

        entry.State = EntityState.Deleted;
    }

    private void DeleteChildrenRecursive(
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
                AttachAsDeletedRecursive(item, currentDepth + 1, maxDepth, visited);
            }
        }
    }

    private static int ClampDepth(int maxDepth) => Math.Min(maxDepth, AbsoluteMaxDepth);
}
