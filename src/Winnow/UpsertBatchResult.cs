namespace Winnow;

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
public class UpsertBatchResult<TKey> : BatchResultBase<TKey> where TKey : notnull, IEquatable<TKey>
{
    private IReadOnlyList<UpsertedEntity<TKey>>? _allUpsertedEntities;
    private IReadOnlyList<TKey>? _insertedIds;
    private IReadOnlyList<TKey>? _updatedIds;
    private IReadOnlyList<TKey>? _successfulIds;

    public IReadOnlyList<UpsertedEntity<TKey>> InsertedEntities { get; init; } = [];
    public IReadOnlyList<UpsertedEntity<TKey>> UpdatedEntities { get; init; } = [];

    public IReadOnlyList<UpsertedEntity<TKey>> AllUpsertedEntities =>
        _allUpsertedEntities ??= InsertedEntities.Concat(UpdatedEntities)
            .OrderBy(e => e.OriginalIndex).ToList();

    public IReadOnlyList<TKey> InsertedIds =>
        _insertedIds ??= InsertedEntities.Select(e => e.Id).ToList();

    public IReadOnlyList<TKey> UpdatedIds =>
        _updatedIds ??= UpdatedEntities.Select(e => e.Id).ToList();

    public IReadOnlyList<TKey> SuccessfulIds =>
        _successfulIds ??= InsertedIds.Concat(UpdatedIds).ToList();

    public int InsertedCount => InsertedEntities.Count;
    public int UpdatedCount => UpdatedEntities.Count;
    public override int SuccessCount => InsertedCount + UpdatedCount;

    public IReadOnlyList<UpsertBatchFailure<TKey>> Failures { get; init; } = [];
    public override int FailureCount => Failures.Count;

    public UpsertedEntity<TKey>? GetByIndex(int originalIndex) =>
        AllUpsertedEntities.FirstOrDefault(e => e.OriginalIndex == originalIndex);

    public UpsertBatchFailure<TKey>? GetFailureByIndex(int originalIndex) =>
        Failures.FirstOrDefault(f => f.EntityIndex == originalIndex);
}
