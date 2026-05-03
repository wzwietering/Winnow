using System.Diagnostics;

namespace Winnow.Internal;

internal static class ResultMerger
{
    internal static WinnowResult<TKey> MergeWinnowResults<TKey>(
        List<WinnowResult<TKey>> results,
        TimeSpan duration,
        int totalRoundTrips,
        int totalRetries) where TKey : notnull, IEquatable<TKey>
    {
        var detail = ResolveDetail<WinnowResult<TKey>>(results, r => r.ResultDetail);
        return new WinnowResult<TKey>
        {
            ResultDetail = detail,
            SuccessfulIds = detail >= ResultDetail.Minimal ? MergeIds(results) : [],
            Failures = detail >= ResultDetail.Minimal ? MergeWinnowFailures(results) : [],
            SuccessCount = results.Sum(r => r.SuccessCount),
            FailureCount = results.Sum(r => r.FailureCount),
            Duration = duration,
            DatabaseRoundTrips = totalRoundTrips,
            WasCancelled = results.Any(r => r.WasCancelled),
            GraphHierarchy = MergeGraphHierarchy<TKey>(results),
            TraversalInfo = MergeTraversalInfoFromResults<TKey>(results),
            TotalRetries = totalRetries
        };
    }

    internal static InsertResult<TKey> MergeInsertResults<TKey>(
        List<(InsertResult<TKey> Result, int Offset)> partitions,
        TimeSpan duration,
        int totalRoundTrips,
        int totalRetries) where TKey : notnull, IEquatable<TKey>
    {
        var detail = ResolveDetail(partitions, p => p.Result.ResultDetail);
        var bases = partitions.Select(p => (WinnowResultBase<TKey>)p.Result);
        return new InsertResult<TKey>
        {
            ResultDetail = detail,
            InsertedEntities = detail >= ResultDetail.Full ? RemapInsertedEntities(partitions) : [],
            InsertedIds = detail >= ResultDetail.Minimal
                ? partitions.SelectMany(p => p.Result.InsertedIdsRaw).ToList()
                : [],
            Failures = detail >= ResultDetail.Minimal ? RemapInsertFailures(partitions) : [],
            SuccessCount = partitions.Sum(p => p.Result.SuccessCount),
            FailureCount = partitions.Sum(p => p.Result.FailureCount),
            Duration = duration,
            DatabaseRoundTrips = totalRoundTrips,
            WasCancelled = partitions.Any(p => p.Result.WasCancelled),
            GraphHierarchy = MergeGraphHierarchy(bases),
            TraversalInfo = MergeTraversalInfoFromResults(bases),
            TotalRetries = totalRetries
        };
    }

    internal static UpsertResult<TKey> MergeUpsertResults<TKey>(
        List<(UpsertResult<TKey> Result, int Offset)> partitions,
        TimeSpan duration,
        int totalRoundTrips,
        int totalRetries) where TKey : notnull, IEquatable<TKey>
    {
        var detail = ResolveDetail(partitions, p => p.Result.ResultDetail);
        var bases = partitions.Select(p => (WinnowResultBase<TKey>)p.Result);
        return new UpsertResult<TKey>
        {
            ResultDetail = detail,
            InsertedEntities = detail >= ResultDetail.Full
                ? RemapUpsertedEntities(partitions, p => p.Result.InsertedEntitiesRaw)
                : [],
            UpdatedEntities = detail >= ResultDetail.Full
                ? RemapUpsertedEntities(partitions, p => p.Result.UpdatedEntitiesRaw)
                : [],
            InsertedIds = detail >= ResultDetail.Minimal
                ? partitions.SelectMany(p => p.Result.InsertedIdsRaw).ToList()
                : [],
            UpdatedIds = detail >= ResultDetail.Minimal
                ? partitions.SelectMany(p => p.Result.UpdatedIdsRaw).ToList()
                : [],
            Failures = detail >= ResultDetail.Minimal ? RemapUpsertFailures(partitions) : [],
            SuccessCount = partitions.Sum(p => p.Result.SuccessCount),
            FailureCount = partitions.Sum(p => p.Result.FailureCount),
            InsertedCount = partitions.Sum(p => p.Result.InsertedCount),
            UpdatedCount = partitions.Sum(p => p.Result.UpdatedCount),
            Duration = duration,
            DatabaseRoundTrips = totalRoundTrips,
            WasCancelled = partitions.Any(p => p.Result.WasCancelled),
            GraphHierarchy = MergeGraphHierarchy(bases),
            TraversalInfo = MergeTraversalInfoFromResults(bases),
            TotalRetries = totalRetries
        };
    }

    private static ResultDetail ResolveDetail<T>(IReadOnlyList<T> items, Func<T, ResultDetail> selector)
    {
        if (items.Count == 0)
            return ResultDetail.Full;
        var detail = selector(items[0]);
        Debug.Assert(
            items.All(i => selector(i) == detail),
            "All partitions must share the same ResultDetail");
        return detail;
    }

    private static List<TKey> MergeIds<TKey>(List<WinnowResult<TKey>> results)
        where TKey : notnull, IEquatable<TKey> =>
        results.SelectMany(r => r.SuccessfulIdsRaw).ToList();

    private static List<WinnowFailure<TKey>> MergeWinnowFailures<TKey>(List<WinnowResult<TKey>> results)
        where TKey : notnull, IEquatable<TKey> =>
        results.SelectMany(r => r.FailuresRaw).ToList();

    private static List<InsertedEntity<TKey>> RemapInsertedEntities<TKey>(
        List<(InsertResult<TKey> Result, int Offset)> partitions)
        where TKey : notnull, IEquatable<TKey>
    {
        return partitions.SelectMany(p => p.Result.InsertedEntitiesRaw.Select(e =>
            new InsertedEntity<TKey>
            {
                Id = e.Id,
                OriginalIndex = e.OriginalIndex + p.Offset,
                Entity = e.Entity
            })).ToList();
    }

    private static List<InsertFailure> RemapInsertFailures<TKey>(
        List<(InsertResult<TKey> Result, int Offset)> partitions)
        where TKey : notnull, IEquatable<TKey>
    {
        return partitions.SelectMany(p => p.Result.FailuresRaw.Select(f =>
            new InsertFailure
            {
                EntityIndex = f.EntityIndex + p.Offset,
                ErrorMessage = f.ErrorMessage,
                Reason = f.Reason,
                Exception = f.Exception
            })).ToList();
    }

    private static List<UpsertedEntity<TKey>> RemapUpsertedEntities<TKey>(
        List<(UpsertResult<TKey> Result, int Offset)> partitions,
        Func<(UpsertResult<TKey> Result, int Offset), IReadOnlyList<UpsertedEntity<TKey>>> selector)
        where TKey : notnull, IEquatable<TKey>
    {
        return partitions.SelectMany(p => selector(p).Select(e =>
            new UpsertedEntity<TKey>
            {
                Id = e.Id,
                OriginalIndex = e.OriginalIndex + p.Offset,
                Entity = e.Entity,
                Operation = e.Operation
            })).ToList();
    }

    private static List<UpsertFailure<TKey>> RemapUpsertFailures<TKey>(
        List<(UpsertResult<TKey> Result, int Offset)> partitions)
        where TKey : notnull, IEquatable<TKey>
    {
        return partitions.SelectMany(p => p.Result.FailuresRaw.Select(f =>
            new UpsertFailure<TKey>
            {
                EntityIndex = f.EntityIndex + p.Offset,
                EntityId = f.EntityId,
                ErrorMessage = f.ErrorMessage,
                Reason = f.Reason,
                Exception = f.Exception,
                AttemptedOperation = f.AttemptedOperation,
                IsDefaultKey = f.IsDefaultKey
            })).ToList();
    }

    private static IReadOnlyList<GraphNode<TKey>>? MergeGraphHierarchy<TKey>(
        IEnumerable<WinnowResultBase<TKey>> results) where TKey : notnull, IEquatable<TKey>
    {
        var hierarchies = results
            .Where(r => r.GraphHierarchyRaw is not null)
            .SelectMany(r => r.GraphHierarchyRaw!)
            .ToList();

        return hierarchies.Count > 0 ? hierarchies : null;
    }

    private static GraphTraversalResult<TKey>? MergeTraversalInfoFromResults<TKey>(
        IEnumerable<WinnowResultBase<TKey>> results) where TKey : notnull, IEquatable<TKey>
    {
        var traversals = results
            .Select(r => r.TraversalInfoRaw)
            .Where(t => t is not null)
            .Cast<GraphTraversalResult<TKey>>()
            .ToList();

        return traversals.Count > 0 ? MergeTraversalInfo(traversals) : null;
    }

    private static GraphTraversalResult<TKey> MergeTraversalInfo<TKey>(
        List<GraphTraversalResult<TKey>> traversals) where TKey : notnull, IEquatable<TKey> => new()
    {
        TotalEntitiesTraversed = traversals.Sum(t => t.TotalEntitiesTraversed),
        MaxDepthReached = traversals.Max(t => t.MaxDepthReached),
        MaxReferenceDepthReached = traversals.Max(t => t.MaxReferenceDepthReached),
        UniqueReferencesProcessed = traversals.Sum(t => t.UniqueReferencesProcessed),
        JoinRecordsCreated = traversals.Sum(t => t.JoinRecordsCreated),
        JoinRecordsRemoved = traversals.Sum(t => t.JoinRecordsRemoved),
        EntitiesByDepth = MergeEntitiesByDepth(traversals),
        JoinOperationsByNavigation = MergeJoinOperations(traversals),
        ProcessedReferencesByType = MergeProcessedReferences<TKey>(traversals)
    };

    private static Dictionary<int, int> MergeEntitiesByDepth<TKey>(
        List<GraphTraversalResult<TKey>> traversals) where TKey : notnull, IEquatable<TKey>
    {
        return traversals
            .SelectMany(t => t.EntitiesByDepth)
            .GroupBy(kv => kv.Key)
            .ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));
    }

    private static Dictionary<string, (int Created, int Removed)> MergeJoinOperations<TKey>(
        List<GraphTraversalResult<TKey>> traversals) where TKey : notnull, IEquatable<TKey>
    {
        return traversals
            .SelectMany(t => t.JoinOperationsByNavigation)
            .GroupBy(kv => kv.Key)
            .ToDictionary(
                g => g.Key,
                g => (g.Sum(kv => kv.Value.Created), g.Sum(kv => kv.Value.Removed)));
    }

    private static Dictionary<string, IReadOnlyList<TKey>> MergeProcessedReferences<TKey>(
        List<GraphTraversalResult<TKey>> traversals) where TKey : notnull, IEquatable<TKey>
    {
        return traversals
            .SelectMany(t => t.ProcessedReferencesByType)
            .GroupBy(kv => kv.Key)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<TKey>)g.SelectMany(kv => kv.Value).ToList());
    }
}
