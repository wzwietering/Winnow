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
/// <para><strong>ResultDetail-gated properties:</strong></para>
/// <para>
/// When <see cref="WinnowResultBase{TKey}.ResultDetail"/> is reduced from the
/// default <see cref="ResultDetail.Full"/>, several properties throw
/// <see cref="InvalidOperationException"/> on access:
/// </para>
/// <list type="bullet">
///   <item>At <see cref="ResultDetail.None"/>: <see cref="InsertedEntities"/>,
///   <see cref="UpdatedEntities"/>, <see cref="AllUpsertedEntities"/>,
///   <see cref="GetByIndex"/>, <see cref="InsertedIds"/>, <see cref="UpdatedIds"/>,
///   <see cref="SuccessfulIds"/>, <see cref="Failures"/>,
///   <see cref="WinnowResultBase{TKey}.GraphHierarchy"/>,
///   <see cref="WinnowResultBase{TKey}.TraversalInfo"/>.</item>
///   <item>At <see cref="ResultDetail.Minimal"/>: the entity collections,
///   <see cref="GetByIndex"/>, and the graph properties throw; ID lists and
///   <see cref="Failures"/> are populated but <see cref="UpsertFailure{TKey}.Exception"/>
///   is null.</item>
/// </list>
/// <para>
/// <see cref="WinnowResultBase{TKey}.SuccessCount"/>,
/// <see cref="WinnowResultBase{TKey}.FailureCount"/>, <see cref="InsertedCount"/>,
/// <see cref="UpdatedCount"/>, <see cref="WinnowResultBase{TKey}.Duration"/>, and
/// <see cref="WinnowResultBase{TKey}.WasCancelled"/> are always available.
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
    private Dictionary<int, UpsertedEntity<TKey>>? _byIndexCache;
    private Dictionary<int, UpsertFailure<TKey>>? _failureByIndexCache;
    private IReadOnlyList<TKey>? _insertedIdsCache;
    private IReadOnlyList<TKey>? _updatedIdsCache;
    private IReadOnlyList<TKey>? _successfulIdsCache;

    /// <summary>
    /// Entities that were inserted — either because they had a default primary key,
    /// or (when <c>MatchBy</c> is configured) because no existing row matched the
    /// configured business key.
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
    /// Entities that were updated — either because they had a non-default primary
    /// key, or (when <c>MatchBy</c> is configured) because an existing row matched
    /// the configured business key.
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
    /// <remarks>
    /// At <see cref="ResultDetail.Full"/>, the IDs are projected from
    /// <see cref="InsertedEntities"/>. At <see cref="ResultDetail.Minimal"/>,
    /// they are tracked directly. Invariant: at most one of the two backing
    /// fields is non-empty. The cache is safe because <see cref="UpsertResult{TKey}"/>
    /// is effectively immutable after construction.
    /// </remarks>
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
    /// <remarks>
    /// Same dual-source pattern as <see cref="InsertedIds"/>; at most one of
    /// the two backing fields is non-empty.
    /// </remarks>
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
    /// Number of entities routed to INSERT because their <c>MatchBy</c> values contained
    /// a null component. <c>null</c> when <c>WithMatchBy</c> was not configured on the
    /// upsert call (the counter is inactive). Otherwise a non-negative integer — including
    /// zero, which means MatchBy ran and observed no null-key entities. A non-zero value
    /// typically signals a data-quality issue upstream (a business key was unexpectedly
    /// missing) and is worth surfacing as a warning rather than relying on the silent
    /// insert.
    /// </summary>
    public int? InsertedWithNullMatchKeyCount { get; init; }

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
    /// than <see cref="ResultDetail.Full"/>. O(1) after the first call.
    /// </summary>
    public UpsertedEntity<TKey>? GetByIndex(int originalIndex)
    {
        if (ResultDetail < ResultDetail.Full)
            throw ResultDetailGuard.NotCaptured(
                nameof(GetByIndex), ResultDetail.Full, ResultDetail, $"{nameof(SuccessfulIds)}");
        _byIndexCache ??= BuildByIndexCache();
        return _byIndexCache.TryGetValue(originalIndex, out var entity) ? entity : null;
    }

    private Dictionary<int, UpsertedEntity<TKey>> BuildByIndexCache()
    {
        var cache = new Dictionary<int, UpsertedEntity<TKey>>(_insertedEntities.Count + _updatedEntities.Count);
        foreach (var entity in _insertedEntities)
            cache[entity.OriginalIndex] = entity;
        foreach (var entity in _updatedEntities)
            cache[entity.OriginalIndex] = entity;
        return cache;
    }

    /// <summary>
    /// Finds a failure by the entity's original input index. Throws when
    /// <see cref="WinnowResultBase{TKey}.ResultDetail"/> is lower than
    /// <see cref="ResultDetail.Minimal"/>. O(1) after the first call.
    /// </summary>
    public UpsertFailure<TKey>? GetFailureByIndex(int originalIndex)
    {
        if (ResultDetail < ResultDetail.Minimal)
            throw ResultDetailGuard.NotCaptured(nameof(GetFailureByIndex), ResultDetail.Minimal, ResultDetail);
        _failureByIndexCache ??= BuildFailureByIndexCache();
        return _failureByIndexCache.TryGetValue(originalIndex, out var failure) ? failure : null;
    }

    private Dictionary<int, UpsertFailure<TKey>> BuildFailureByIndexCache()
    {
        var cache = new Dictionary<int, UpsertFailure<TKey>>(_failures.Count);
        foreach (var failure in _failures)
            cache[failure.EntityIndex] = failure;
        return cache;
    }

    /// <inheritdoc />
    protected override int GetCollectionSuccessCount()
    {
        var entityCount = _insertedEntities.Count + _updatedEntities.Count;
        return entityCount > 0 ? entityCount : _explicitInsertedIds.Count + _explicitUpdatedIds.Count;
    }

    /// <inheritdoc />
    protected override int GetCollectionFailureCount() => _failures.Count;
}
