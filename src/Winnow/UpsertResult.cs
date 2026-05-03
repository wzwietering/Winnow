using Winnow.Internal;

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
    private readonly IReadOnlyList<UpsertedEntity<TKey>> _insertedEntities = [];
    private readonly IReadOnlyList<UpsertedEntity<TKey>> _updatedEntities = [];
    private readonly IReadOnlyList<TKey> _explicitInsertedIds = [];
    private readonly IReadOnlyList<TKey> _explicitUpdatedIds = [];
    private readonly IReadOnlyList<UpsertFailure<TKey>> _failures = [];
    private IReadOnlyList<UpsertedEntity<TKey>>? _allUpsertedEntitiesCache;
    private IReadOnlyList<TKey>? _insertedIdsCache;
    private IReadOnlyList<TKey>? _updatedIdsCache;
    private IReadOnlyList<TKey>? _successfulIdsCache;

    /// <summary>
    /// Entities that were inserted (had default key values).
    /// Throws when <see cref="WinnowResultBase{TKey}.ResultDetail"/> is lower
    /// than <see cref="ResultDetail.Full"/>.
    /// </summary>
    public IReadOnlyList<UpsertedEntity<TKey>> InsertedEntities
    {
        get => ResultDetail >= ResultDetail.Full
            ? _insertedEntities
            : throw ResultDetailGuard.NotCaptured(
                nameof(InsertedEntities), ResultDetail.Full, ResultDetail, $"{nameof(InsertedIds)}");
        init => _insertedEntities = value ?? [];
    }

    internal IReadOnlyList<UpsertedEntity<TKey>> InsertedEntitiesRaw => _insertedEntities;

    /// <summary>
    /// Entities that were updated (had non-default key values).
    /// Throws when <see cref="WinnowResultBase{TKey}.ResultDetail"/> is lower
    /// than <see cref="ResultDetail.Full"/>.
    /// </summary>
    public IReadOnlyList<UpsertedEntity<TKey>> UpdatedEntities
    {
        get => ResultDetail >= ResultDetail.Full
            ? _updatedEntities
            : throw ResultDetailGuard.NotCaptured(
                nameof(UpdatedEntities), ResultDetail.Full, ResultDetail, $"{nameof(UpdatedIds)}");
        init => _updatedEntities = value ?? [];
    }

    internal IReadOnlyList<UpsertedEntity<TKey>> UpdatedEntitiesRaw => _updatedEntities;

    /// <summary>
    /// All upserted entities (inserted + updated), ordered by original index.
    /// Throws when <see cref="WinnowResultBase{TKey}.ResultDetail"/> is lower
    /// than <see cref="ResultDetail.Full"/>.
    /// </summary>
    public IReadOnlyList<UpsertedEntity<TKey>> AllUpsertedEntities
    {
        get
        {
            if (ResultDetail < ResultDetail.Full)
                throw ResultDetailGuard.NotCaptured(
                    nameof(AllUpsertedEntities), ResultDetail.Full, ResultDetail, $"{nameof(SuccessfulIds)}");
            return _allUpsertedEntitiesCache ??= _insertedEntities.Concat(_updatedEntities)
                .OrderBy(e => e.OriginalIndex).ToList();
        }
    }

    /// <summary>
    /// IDs of entities that were inserted. Throws when
    /// <see cref="WinnowResultBase{TKey}.ResultDetail"/> is lower than
    /// <see cref="ResultDetail.Minimal"/>.
    /// </summary>
    public IReadOnlyList<TKey> InsertedIds
    {
        get
        {
            if (ResultDetail < ResultDetail.Minimal)
                throw ResultDetailGuard.NotCaptured(nameof(InsertedIds), ResultDetail.Minimal, ResultDetail);
            return _insertedIdsCache ??= _insertedEntities.Count > 0
                ? _insertedEntities.Select(e => e.Id).ToList()
                : _explicitInsertedIds;
        }
        init => _explicitInsertedIds = value ?? [];
    }

    internal IReadOnlyList<TKey> InsertedIdsRaw => _explicitInsertedIds;

    /// <summary>
    /// IDs of entities that were updated. Throws when
    /// <see cref="WinnowResultBase{TKey}.ResultDetail"/> is lower than
    /// <see cref="ResultDetail.Minimal"/>.
    /// </summary>
    public IReadOnlyList<TKey> UpdatedIds
    {
        get
        {
            if (ResultDetail < ResultDetail.Minimal)
                throw ResultDetailGuard.NotCaptured(nameof(UpdatedIds), ResultDetail.Minimal, ResultDetail);
            return _updatedIdsCache ??= _updatedEntities.Count > 0
                ? _updatedEntities.Select(e => e.Id).ToList()
                : _explicitUpdatedIds;
        }
        init => _explicitUpdatedIds = value ?? [];
    }

    internal IReadOnlyList<TKey> UpdatedIdsRaw => _explicitUpdatedIds;

    /// <summary>
    /// IDs of all successfully processed entities (inserted + updated).
    /// Throws when <see cref="WinnowResultBase{TKey}.ResultDetail"/> is lower
    /// than <see cref="ResultDetail.Minimal"/>.
    /// </summary>
    public IReadOnlyList<TKey> SuccessfulIds
    {
        get
        {
            if (ResultDetail < ResultDetail.Minimal)
                throw ResultDetailGuard.NotCaptured(nameof(SuccessfulIds), ResultDetail.Minimal, ResultDetail);
            return _successfulIdsCache ??= InsertedIds.Concat(UpdatedIds).ToList();
        }
    }

    /// <summary>
    /// Number of entities that were inserted.
    /// </summary>
    public int InsertedCount { get; init; }

    /// <summary>
    /// Number of entities that were updated.
    /// </summary>
    public int UpdatedCount { get; init; }

    /// <summary>
    /// Details of each failed upsert operation. Throws when
    /// <see cref="WinnowResultBase{TKey}.ResultDetail"/> is lower than
    /// <see cref="ResultDetail.Minimal"/>. At <see cref="ResultDetail.Minimal"/>
    /// the <see cref="UpsertFailure{TKey}.Exception"/> is null.
    /// </summary>
    public IReadOnlyList<UpsertFailure<TKey>> Failures
    {
        get => ResultDetail >= ResultDetail.Minimal
            ? _failures
            : throw ResultDetailGuard.NotCaptured(nameof(Failures), ResultDetail.Minimal, ResultDetail);
        init => _failures = value ?? [];
    }

    internal IReadOnlyList<UpsertFailure<TKey>> FailuresRaw => _failures;

    /// <summary>
    /// Finds a successfully upserted entity by its original input index.
    /// Throws when <see cref="WinnowResultBase{TKey}.ResultDetail"/> is lower
    /// than <see cref="ResultDetail.Full"/>.
    /// </summary>
    public UpsertedEntity<TKey>? GetByIndex(int originalIndex) =>
        AllUpsertedEntities.FirstOrDefault(e => e.OriginalIndex == originalIndex);

    /// <summary>
    /// Finds a failure by the entity's original input index. Throws when
    /// <see cref="WinnowResultBase{TKey}.ResultDetail"/> is lower than
    /// <see cref="ResultDetail.Minimal"/>.
    /// </summary>
    public UpsertFailure<TKey>? GetFailureByIndex(int originalIndex) =>
        Failures.FirstOrDefault(f => f.EntityIndex == originalIndex);

    /// <inheritdoc />
    protected override int GetCollectionSuccessCount()
    {
        var entityCount = _insertedEntities.Count + _updatedEntities.Count;
        return entityCount > 0 ? entityCount : _explicitInsertedIds.Count + _explicitUpdatedIds.Count;
    }

    /// <inheritdoc />
    protected override int GetCollectionFailureCount() => _failures.Count;
}
