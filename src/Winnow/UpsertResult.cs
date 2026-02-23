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
public class UpsertResult<TKey> : WinnowResultBase<TKey> where TKey : notnull, IEquatable<TKey>
{
    private IReadOnlyList<UpsertedEntity<TKey>>? _allUpsertedEntities;
    private IReadOnlyList<TKey>? _insertedIds;
    private IReadOnlyList<TKey>? _updatedIds;
    private IReadOnlyList<TKey>? _successfulIds;

    /// <summary>
    /// Entities that were inserted (had default key values).
    /// </summary>
    public IReadOnlyList<UpsertedEntity<TKey>> InsertedEntities { get; init; } = [];

    /// <summary>
    /// Entities that were updated (had non-default key values).
    /// </summary>
    public IReadOnlyList<UpsertedEntity<TKey>> UpdatedEntities { get; init; } = [];

    /// <summary>
    /// All upserted entities (inserted + updated), ordered by original index.
    /// </summary>
    public IReadOnlyList<UpsertedEntity<TKey>> AllUpsertedEntities =>
        _allUpsertedEntities ??= InsertedEntities.Concat(UpdatedEntities)
            .OrderBy(e => e.OriginalIndex).ToList();

    /// <summary>
    /// IDs of entities that were inserted.
    /// </summary>
    public IReadOnlyList<TKey> InsertedIds =>
        _insertedIds ??= InsertedEntities.Select(e => e.Id).ToList();

    /// <summary>
    /// IDs of entities that were updated.
    /// </summary>
    public IReadOnlyList<TKey> UpdatedIds =>
        _updatedIds ??= UpdatedEntities.Select(e => e.Id).ToList();

    /// <summary>
    /// IDs of all successfully processed entities (inserted + updated).
    /// </summary>
    public IReadOnlyList<TKey> SuccessfulIds =>
        _successfulIds ??= InsertedIds.Concat(UpdatedIds).ToList();

    /// <summary>
    /// Number of entities that were inserted.
    /// </summary>
    public int InsertedCount => InsertedEntities.Count;

    /// <summary>
    /// Number of entities that were updated.
    /// </summary>
    public int UpdatedCount => UpdatedEntities.Count;

    /// <inheritdoc />
    public override int SuccessCount => InsertedCount + UpdatedCount;

    /// <summary>
    /// Details of each failed upsert operation.
    /// </summary>
    public IReadOnlyList<UpsertFailure<TKey>> Failures { get; init; } = [];

    /// <inheritdoc />
    public override int FailureCount => Failures.Count;

    /// <summary>
    /// Finds a successfully upserted entity by its original input index.
    /// </summary>
    public UpsertedEntity<TKey>? GetByIndex(int originalIndex) =>
        AllUpsertedEntities.FirstOrDefault(e => e.OriginalIndex == originalIndex);

    /// <summary>
    /// Finds a failure by the entity's original input index.
    /// </summary>
    public UpsertFailure<TKey>? GetFailureByIndex(int originalIndex) =>
        Failures.FirstOrDefault(f => f.EntityIndex == originalIndex);
}
