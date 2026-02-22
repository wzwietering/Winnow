namespace Winnow.Internal;

/// <summary>
/// Tracks statistics for many-to-many join record operations.
/// </summary>
internal class ManyToManyStatisticsTracker
{
    private int _totalCreated;
    private int _totalRemoved;
    private readonly Dictionary<string, (int Created, int Removed)> _byNavigation = [];

    internal void RecordJoinCreated(string entityTypeName, string navigationName)
    {
        _totalCreated++;
        UpdateNavigationStats(entityTypeName, navigationName, created: 1, removed: 0);
    }

    internal void RecordJoinRemoved(string entityTypeName, string navigationName)
    {
        _totalRemoved++;
        UpdateNavigationStats(entityTypeName, navigationName, created: 0, removed: 1);
    }

    private void UpdateNavigationStats(
        string entityTypeName, string navigationName, int created, int removed)
    {
        var key = $"{entityTypeName}.{navigationName}";
        _byNavigation.TryGetValue(key, out var existing);
        _byNavigation[key] = (existing.Created + created, existing.Removed + removed);
    }

    internal (int TotalCreated, int TotalRemoved, IReadOnlyDictionary<string, (int Created, int Removed)> ByNavigation) GetStatistics() =>
        (_totalCreated, _totalRemoved, _byNavigation);

    internal void Reset()
    {
        _totalCreated = 0;
        _totalRemoved = 0;
        _byNavigation.Clear();
    }

    internal void Merge(ManyToManyStatisticsTracker other)
    {
        var (created, removed, byNav) = other.GetStatistics();
        _totalCreated += created;
        _totalRemoved += removed;

        foreach (var (key, stats) in byNav)
        {
            _byNavigation.TryGetValue(key, out var existing);
            _byNavigation[key] = (existing.Created + stats.Created, existing.Removed + stats.Removed);
        }
    }
}
