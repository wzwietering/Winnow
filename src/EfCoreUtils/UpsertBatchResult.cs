namespace EfCoreUtils;

/// <summary>
/// Result of an upsert batch operation, tracking which entities were inserted vs updated.
/// </summary>
/// <remarks>
/// <para><strong>NOT a database MERGE:</strong></para>
/// <para>
/// This is NOT an atomic database-level upsert (MERGE/INSERT ON CONFLICT).
/// It performs conditional INSERT or UPDATE operations based on key detection.
/// </para>
/// </remarks>
public class UpsertBatchResult<TKey> where TKey : notnull, IEquatable<TKey>
{
    // === ENTITY TRACKING ===

    public IReadOnlyList<UpsertedEntity<TKey>> InsertedEntities { get; init; } = [];
    public IReadOnlyList<UpsertedEntity<TKey>> UpdatedEntities { get; init; } = [];

    public IReadOnlyList<UpsertedEntity<TKey>> AllUpsertedEntities =>
        InsertedEntities.Concat(UpdatedEntities).OrderBy(e => e.OriginalIndex).ToList();

    // === ID CONVENIENCE ===

    public IReadOnlyList<TKey> InsertedIds => InsertedEntities.Select(e => e.Id).ToList();
    public IReadOnlyList<TKey> UpdatedIds => UpdatedEntities.Select(e => e.Id).ToList();
    public IReadOnlyList<TKey> SuccessfulIds => InsertedIds.Concat(UpdatedIds).ToList();

    // === COUNTS ===

    public int InsertedCount => InsertedEntities.Count;
    public int UpdatedCount => UpdatedEntities.Count;
    public int SuccessCount => InsertedCount + UpdatedCount;

    // === FAILURES ===

    public IReadOnlyList<UpsertBatchFailure<TKey>> Failures { get; init; } = [];
    public int FailureCount => Failures.Count;

    // === TOTALS ===

    public int TotalProcessed => SuccessCount + FailureCount;
    public double SuccessRate => TotalProcessed > 0 ? (double)SuccessCount / TotalProcessed : 0;

    // === TIMING ===

    public TimeSpan Duration { get; init; }
    public int DatabaseRoundTrips { get; init; }

    // === GRAPH SUPPORT ===

    /// <summary>
    /// For graph upserts only: Full hierarchy of upserted entities keyed by root ID.
    /// Null for parent-only UpsertBatch operations.
    /// </summary>
    public IReadOnlyDictionary<TKey, GraphNode<TKey>>? GraphHierarchy { get; init; }

    /// <summary>
    /// For graph upserts only: Statistics about the traversal.
    /// Null for parent-only UpsertBatch operations.
    /// </summary>
    public GraphTraversalResult<TKey>? TraversalInfo { get; init; }

    // === STATUS HELPERS ===

    public bool IsCompleteSuccess => FailureCount == 0 && SuccessCount > 0;
    public bool IsCompleteFailure => SuccessCount == 0 && FailureCount > 0;
    public bool IsPartialSuccess => SuccessCount > 0 && FailureCount > 0;

    // === CONVENIENCE METHODS ===

    public UpsertedEntity<TKey>? GetByIndex(int originalIndex) =>
        AllUpsertedEntities.FirstOrDefault(e => e.OriginalIndex == originalIndex);

    public UpsertBatchFailure<TKey>? GetFailureByIndex(int originalIndex) =>
        Failures.FirstOrDefault(f => f.EntityIndex == originalIndex);
}
