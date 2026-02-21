namespace EfCoreUtils.Internal;

internal static class BatchResultMerger
{
    internal static BatchResult<TKey> MergeBatchResults<TKey>(
        List<BatchResult<TKey>> results,
        TimeSpan duration,
        int totalRoundTrips) where TKey : notnull, IEquatable<TKey> => new()
    {
        SuccessfulIds = results.SelectMany(r => r.SuccessfulIds).ToList(),
        Failures = results.SelectMany(r => r.Failures).ToList(),
        Duration = duration,
        DatabaseRoundTrips = totalRoundTrips,
        WasCancelled = results.Any(r => r.WasCancelled),
        GraphHierarchy = MergeGraphHierarchy<TKey>(results.Cast<BatchResultBase<TKey>>()),
        TraversalInfo = MergeTraversalInfoFromResults<TKey>(results.Cast<BatchResultBase<TKey>>()),
        TotalRetries = results.Sum(r => r.TotalRetries)
    };

    internal static InsertBatchResult<TKey> MergeInsertResults<TKey>(
        List<(InsertBatchResult<TKey> Result, int Offset)> partitions,
        TimeSpan duration,
        int totalRoundTrips) where TKey : notnull, IEquatable<TKey> => new()
    {
        InsertedEntities = RemapInsertedEntities<TKey>(partitions),
        Failures = RemapInsertFailures(partitions),
        Duration = duration,
        DatabaseRoundTrips = totalRoundTrips,
        WasCancelled = partitions.Any(p => p.Result.WasCancelled),
        GraphHierarchy = MergeGraphHierarchy<TKey>(partitions.Select(p => (BatchResultBase<TKey>)p.Result)),
        TraversalInfo = MergeTraversalInfoFromResults<TKey>(partitions.Select(p => (BatchResultBase<TKey>)p.Result)),
        TotalRetries = partitions.Sum(p => p.Result.TotalRetries)
    };

    internal static UpsertBatchResult<TKey> MergeUpsertResults<TKey>(
        List<(UpsertBatchResult<TKey> Result, int Offset)> partitions,
        TimeSpan duration,
        int totalRoundTrips) where TKey : notnull, IEquatable<TKey> => new()
    {
        InsertedEntities = RemapUpsertedEntities<TKey>(partitions, e => e.InsertedEntities),
        UpdatedEntities = RemapUpsertedEntities<TKey>(partitions, e => e.UpdatedEntities),
        Failures = RemapUpsertFailures<TKey>(partitions),
        Duration = duration,
        DatabaseRoundTrips = totalRoundTrips,
        WasCancelled = partitions.Any(p => p.Result.WasCancelled),
        GraphHierarchy = MergeGraphHierarchy<TKey>(partitions.Select(p => (BatchResultBase<TKey>)p.Result)),
        TraversalInfo = MergeTraversalInfoFromResults<TKey>(partitions.Select(p => (BatchResultBase<TKey>)p.Result)),
        TotalRetries = partitions.Sum(p => p.Result.TotalRetries)
    };

    private static List<InsertedEntity<TKey>> RemapInsertedEntities<TKey>(
        List<(InsertBatchResult<TKey> Result, int Offset)> partitions)
        where TKey : notnull, IEquatable<TKey>
    {
        return partitions.SelectMany(p => p.Result.InsertedEntities.Select(e =>
            new InsertedEntity<TKey>
            {
                Id = e.Id,
                OriginalIndex = e.OriginalIndex + p.Offset,
                Entity = e.Entity
            })).ToList();
    }

    private static List<InsertBatchFailure> RemapInsertFailures<TKey>(
        List<(InsertBatchResult<TKey> Result, int Offset)> partitions)
        where TKey : notnull, IEquatable<TKey>
    {
        return partitions.SelectMany(p => p.Result.Failures.Select(f =>
            new InsertBatchFailure
            {
                EntityIndex = f.EntityIndex + p.Offset,
                ErrorMessage = f.ErrorMessage,
                Reason = f.Reason,
                Exception = f.Exception
            })).ToList();
    }

    private static List<UpsertedEntity<TKey>> RemapUpsertedEntities<TKey>(
        List<(UpsertBatchResult<TKey> Result, int Offset)> partitions,
        Func<UpsertBatchResult<TKey>, IReadOnlyList<UpsertedEntity<TKey>>> selector)
        where TKey : notnull, IEquatable<TKey>
    {
        return partitions.SelectMany(p => selector(p.Result).Select(e =>
            new UpsertedEntity<TKey>
            {
                Id = e.Id,
                OriginalIndex = e.OriginalIndex + p.Offset,
                Entity = e.Entity,
                Operation = e.Operation
            })).ToList();
    }

    private static List<UpsertBatchFailure<TKey>> RemapUpsertFailures<TKey>(
        List<(UpsertBatchResult<TKey> Result, int Offset)> partitions)
        where TKey : notnull, IEquatable<TKey>
    {
        return partitions.SelectMany(p => p.Result.Failures.Select(f =>
            new UpsertBatchFailure<TKey>
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
        IEnumerable<BatchResultBase<TKey>> results) where TKey : notnull, IEquatable<TKey>
    {
        var hierarchies = results
            .Where(r => r.GraphHierarchy is not null)
            .SelectMany(r => r.GraphHierarchy!)
            .ToList();

        return hierarchies.Count > 0 ? hierarchies : null;
    }

    private static GraphTraversalResult<TKey>? MergeTraversalInfoFromResults<TKey>(
        IEnumerable<BatchResultBase<TKey>> results) where TKey : notnull, IEquatable<TKey>
    {
        var traversals = results
            .Select(r => r.TraversalInfo)
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
