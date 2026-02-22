using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Winnow.Internal.Services;

/// <summary>
/// Service for handling many-to-many relationship removal during delete operations.
/// </summary>
internal class ManyToManyDeleteProcessor<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly DbContext _context;

    internal ManyToManyDeleteProcessor(DbContext context)
    {
        _context = context;
    }

    internal ManyToManyStatisticsTracker ProcessManyToManyForDelete(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var tracker = new ManyToManyStatisticsTracker();
        var entry = _context.Entry(entity);

        RemoveJoinRecords(entry, tracker);
        return tracker;
    }

    private void RemoveJoinRecords(EntityEntry entry, ManyToManyStatisticsTracker tracker)
    {
        var entityTypeName = entry.Metadata.ClrType.Name;

        foreach (var navigation in ManyToManyNavigationHelper.GetManyToManyNavigations(entry))
        {
            var navigationName = navigation.Metadata.Name;
            var itemCount = NavigationPropertyHelper.GetCollectionItemCount(navigation);

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
}
