using Winnow.Internal.Services;
using Winnow.Internal.Validation;

namespace Winnow.Internal;

/// <summary>
/// Builds per-result-type failure lists from the <see cref="WinnowEntityFailure"/>
/// records carried inside a <see cref="WinnowValidationException"/>, then merges
/// them with a survivor-re-run result so the
/// <see cref="ValidationFailureBehavior.Throw"/> recovery path in parallel mode
/// produces a result equivalent to the single-context path.
/// </summary>
/// <remarks>
/// <para>
/// <b>FailureCount accuracy:</b> <see cref="WinnowResultBase{TKey}.FailureCount"/>
/// must remain correct at every <see cref="ResultDetail"/> level. The
/// <c>Build*</c> helpers gate the populated failure list on
/// <see cref="ResultDetail.Minimal"/>, but the merge arithmetic uses
/// <c>failures.Count</c> (the source-of-truth count from the validation exception)
/// rather than the gated list's <see cref="List{T}.Count"/>. Without this, the
/// count would silently drop to zero under <see cref="ResultDetail.None"/>.
/// </para>
/// <para>
/// Survivor results carry indices relative to the survivor sub-list. The
/// <c>RemapInsert*</c> / <c>RemapUpsert*</c> helpers translate those back to
/// partition-relative positions so the top-level <see cref="ResultMerger"/> can
/// add the partition offset and reach the user-visible input position.
/// </para>
/// </remarks>
internal static class ValidationResultMerger
{
    internal static WinnowResult<TKey> MergeWinnow<TEntity, TKey>(
        WinnowResult<TKey>? survivor,
        List<TEntity> partition,
        IReadOnlyList<WinnowEntityFailure> failures,
        ResultDetail resultDetail,
        EntityKeyService<TEntity, TKey> keyService)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        var validationFailures = BuildWinnowFailures(partition, failures, resultDetail, keyService);
        var validationCount = failures.Count;
        if (survivor is null)
        {
            return new WinnowResult<TKey>
            {
                ResultDetail = resultDetail,
                SuccessfulIds = [],
                Failures = validationFailures,
                SuccessCount = 0,
                FailureCount = validationCount,
            };
        }
        return new WinnowResult<TKey>
        {
            ResultDetail = survivor.ResultDetail,
            SuccessfulIds = survivor.SuccessfulIdsRaw,
            Failures = [.. survivor.FailuresRaw, .. validationFailures],
            SuccessCount = survivor.SuccessCount,
            FailureCount = survivor.FailureCount + validationCount,
            Duration = survivor.Duration,
            DatabaseRoundTrips = survivor.DatabaseRoundTrips,
            WasCancelled = survivor.WasCancelled,
            TotalRetries = survivor.TotalRetries,
            GraphHierarchy = survivor.GraphHierarchyRaw,
            TraversalInfo = survivor.TraversalInfoRaw,
        };
    }

    internal static InsertResult<TKey> MergeInsert<TKey>(
        InsertResult<TKey>? survivor,
        int[] survivorOriginalIndices,
        IReadOnlyList<WinnowEntityFailure> failures,
        ResultDetail resultDetail)
        where TKey : notnull, IEquatable<TKey>
    {
        var remapped = RemapInsertSurvivor(survivor, survivorOriginalIndices);
        var validationFailures = BuildInsertFailures(failures, resultDetail);
        var validationCount = failures.Count;
        if (remapped is null)
        {
            return new InsertResult<TKey>
            {
                ResultDetail = resultDetail,
                InsertedEntities = [],
                Failures = validationFailures,
                SuccessCount = 0,
                FailureCount = validationCount,
            };
        }
        return new InsertResult<TKey>
        {
            ResultDetail = remapped.ResultDetail,
            InsertedEntities = remapped.InsertedEntitiesRaw,
            InsertedIds = remapped.InsertedIdsRaw,
            Failures = [.. remapped.FailuresRaw, .. validationFailures],
            SuccessCount = remapped.SuccessCount,
            FailureCount = remapped.FailureCount + validationCount,
            Duration = remapped.Duration,
            DatabaseRoundTrips = remapped.DatabaseRoundTrips,
            WasCancelled = remapped.WasCancelled,
            TotalRetries = remapped.TotalRetries,
            GraphHierarchy = remapped.GraphHierarchyRaw,
            TraversalInfo = remapped.TraversalInfoRaw,
        };
    }

    internal static UpsertResult<TKey> MergeUpsert<TEntity, TKey>(
        UpsertResult<TKey>? survivor,
        int[] survivorOriginalIndices,
        List<TEntity> partition,
        IReadOnlyList<WinnowEntityFailure> failures,
        ResultDetail resultDetail,
        EntityKeyService<TEntity, TKey> keyService)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        var remapped = RemapUpsertSurvivor(survivor, survivorOriginalIndices);
        var validationFailures = BuildUpsertFailures(partition, failures, resultDetail, keyService);
        var validationCount = failures.Count;
        if (remapped is null)
        {
            return new UpsertResult<TKey>
            {
                ResultDetail = resultDetail,
                InsertedEntities = [],
                UpdatedEntities = [],
                Failures = validationFailures,
                SuccessCount = 0,
                FailureCount = validationCount,
                InsertedCount = 0,
                UpdatedCount = 0,
            };
        }
        return new UpsertResult<TKey>
        {
            ResultDetail = remapped.ResultDetail,
            InsertedEntities = remapped.InsertedEntitiesRaw,
            UpdatedEntities = remapped.UpdatedEntitiesRaw,
            InsertedIds = remapped.InsertedIdsRaw,
            UpdatedIds = remapped.UpdatedIdsRaw,
            Failures = [.. remapped.FailuresRaw, .. validationFailures],
            SuccessCount = remapped.SuccessCount,
            FailureCount = remapped.FailureCount + validationCount,
            InsertedCount = remapped.InsertedCount,
            UpdatedCount = remapped.UpdatedCount,
            InsertedWithNullMatchKeyCount = remapped.InsertedWithNullMatchKeyCount,
            Duration = remapped.Duration,
            DatabaseRoundTrips = remapped.DatabaseRoundTrips,
            WasCancelled = remapped.WasCancelled,
            TotalRetries = remapped.TotalRetries,
            GraphHierarchy = remapped.GraphHierarchyRaw,
            TraversalInfo = remapped.TraversalInfoRaw,
        };
    }

    private static List<WinnowFailure<TKey>> BuildWinnowFailures<TEntity, TKey>(
        List<TEntity> partition,
        IReadOnlyList<WinnowEntityFailure> failures,
        ResultDetail resultDetail,
        EntityKeyService<TEntity, TKey> keyService)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        if (resultDetail < ResultDetail.Minimal) return [];
        var list = new List<WinnowFailure<TKey>>(failures.Count);
        foreach (var f in failures)
        {
            list.Add(new WinnowFailure<TKey>
            {
                EntityId = SafeReadKey(partition[f.EntityIndex], keyService),
                ErrorMessage = f.ErrorMessage,
                Reason = FailureReason.PreValidationError,
                ValidationErrors = f.ValidationErrors,
            });
        }
        return list;
    }

    private static List<InsertFailure> BuildInsertFailures(
        IReadOnlyList<WinnowEntityFailure> failures, ResultDetail resultDetail)
    {
        if (resultDetail < ResultDetail.Minimal) return [];
        var list = new List<InsertFailure>(failures.Count);
        foreach (var f in failures)
        {
            list.Add(new InsertFailure
            {
                EntityIndex = f.EntityIndex,
                ErrorMessage = f.ErrorMessage,
                Reason = FailureReason.PreValidationError,
                ValidationErrors = f.ValidationErrors,
            });
        }
        return list;
    }

    private static List<UpsertFailure<TKey>> BuildUpsertFailures<TEntity, TKey>(
        List<TEntity> partition,
        IReadOnlyList<WinnowEntityFailure> failures,
        ResultDetail resultDetail,
        EntityKeyService<TEntity, TKey> keyService)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        if (resultDetail < ResultDetail.Minimal) return [];
        var list = new List<UpsertFailure<TKey>>(failures.Count);
        foreach (var f in failures)
        {
            var (attempted, isDefaultKey, entityId) = ClassifyUpsertFailure(partition[f.EntityIndex], keyService);
            list.Add(new UpsertFailure<TKey>
            {
                EntityIndex = f.EntityIndex,
                EntityId = entityId,
                ErrorMessage = f.ErrorMessage,
                Reason = FailureReason.PreValidationError,
                AttemptedOperation = attempted,
                IsDefaultKey = isDefaultKey,
                ValidationErrors = f.ValidationErrors,
            });
        }
        return list;
    }

    private static TKey SafeReadKey<TEntity, TKey>(TEntity? entity, EntityKeyService<TEntity, TKey> keyService)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        if (entity is null) return default!;
        try
        {
            return keyService.GetEntityIdFromInstance(entity);
        }
        catch (InvalidOperationException ex) when (EfExceptionFilter.IsEntityFrameworkInvalidOperation(ex))
        {
            return default!;
        }
    }

    /// <summary>
    /// Classifies a validation-failed upsert entity as INSERT vs UPDATE by reading
    /// its primary-key CLR properties through the cached
    /// <see cref="EntityKeyService{TEntity, TKey}"/>. Mirrors
    /// <c>OperationPreValidationHelper.RecordUpsertFailure</c> so the parallel
    /// <see cref="ValidationFailureBehavior.Throw"/> recovery path produces
    /// equivalent <see cref="UpsertFailure{TKey}"/> shapes.
    /// </summary>
    private static (UpsertOperationType Attempted, bool IsDefaultKey, TKey EntityId) ClassifyUpsertFailure<TEntity, TKey>(
        TEntity? entity, EntityKeyService<TEntity, TKey> keyService)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        if (entity is null)
            return (UpsertOperationType.Insert, IsDefaultKey: true, default!);

        bool isDefault;
        try
        {
            isDefault = keyService.HasDefaultKeyValueFromInstance(entity);
        }
        catch (InvalidOperationException ex) when (EfExceptionFilter.IsEntityFrameworkInvalidOperation(ex))
        {
            return (UpsertOperationType.Insert, IsDefaultKey: true, default!);
        }
        var entityId = isDefault ? default! : SafeReadKey(entity, keyService);
        return (isDefault ? UpsertOperationType.Insert : UpsertOperationType.Update, isDefault, entityId);
    }

    /// <summary>
    /// Translates EntityIndex / OriginalIndex on a survivor-only result from
    /// survivor-list positions (0..N-1) back to partition-relative positions
    /// so the top-level <see cref="ResultMerger"/> can add the partition offset
    /// to reach the user-visible input position.
    /// </summary>
    private static InsertResult<TKey>? RemapInsertSurvivor<TKey>(
        InsertResult<TKey>? survivor, int[] survivorOriginalIndices)
        where TKey : notnull, IEquatable<TKey>
    {
        if (survivor is null) return null;
        return new InsertResult<TKey>
        {
            ResultDetail = survivor.ResultDetail,
            InsertedEntities = RemapInsertedEntities(survivor.InsertedEntitiesRaw, survivorOriginalIndices),
            InsertedIds = survivor.InsertedIdsRaw,
            Failures = RemapInsertFailures(survivor.FailuresRaw, survivorOriginalIndices),
            SuccessCount = survivor.SuccessCount,
            FailureCount = survivor.FailureCount,
            Duration = survivor.Duration,
            DatabaseRoundTrips = survivor.DatabaseRoundTrips,
            WasCancelled = survivor.WasCancelled,
            TotalRetries = survivor.TotalRetries,
            GraphHierarchy = survivor.GraphHierarchyRaw,
            TraversalInfo = survivor.TraversalInfoRaw,
        };
    }

    private static UpsertResult<TKey>? RemapUpsertSurvivor<TKey>(
        UpsertResult<TKey>? survivor, int[] survivorOriginalIndices)
        where TKey : notnull, IEquatable<TKey>
    {
        if (survivor is null) return null;
        return new UpsertResult<TKey>
        {
            ResultDetail = survivor.ResultDetail,
            InsertedEntities = RemapUpsertedEntities(survivor.InsertedEntitiesRaw, survivorOriginalIndices),
            UpdatedEntities = RemapUpsertedEntities(survivor.UpdatedEntitiesRaw, survivorOriginalIndices),
            InsertedIds = survivor.InsertedIdsRaw,
            UpdatedIds = survivor.UpdatedIdsRaw,
            Failures = RemapUpsertFailures(survivor.FailuresRaw, survivorOriginalIndices),
            SuccessCount = survivor.SuccessCount,
            FailureCount = survivor.FailureCount,
            InsertedCount = survivor.InsertedCount,
            UpdatedCount = survivor.UpdatedCount,
            InsertedWithNullMatchKeyCount = survivor.InsertedWithNullMatchKeyCount,
            Duration = survivor.Duration,
            DatabaseRoundTrips = survivor.DatabaseRoundTrips,
            WasCancelled = survivor.WasCancelled,
            TotalRetries = survivor.TotalRetries,
            GraphHierarchy = survivor.GraphHierarchyRaw,
            TraversalInfo = survivor.TraversalInfoRaw,
        };
    }

    private static List<InsertedEntity<TKey>> RemapInsertedEntities<TKey>(
        IReadOnlyList<InsertedEntity<TKey>> entities, int[] survivorOriginalIndices)
        where TKey : notnull, IEquatable<TKey>
    {
        var list = new List<InsertedEntity<TKey>>(entities.Count);
        foreach (var e in entities)
        {
            list.Add(new InsertedEntity<TKey>
            {
                Id = e.Id,
                OriginalIndex = survivorOriginalIndices[e.OriginalIndex],
                Entity = e.Entity,
            });
        }
        return list;
    }

    private static List<InsertFailure> RemapInsertFailures(
        IReadOnlyList<InsertFailure> failures, int[] survivorOriginalIndices)
    {
        var list = new List<InsertFailure>(failures.Count);
        foreach (var f in failures)
        {
            list.Add(new InsertFailure
            {
                EntityIndex = survivorOriginalIndices[f.EntityIndex],
                ErrorMessage = f.ErrorMessage,
                Reason = f.Reason,
                Exception = f.Exception,
                ValidationErrors = f.ValidationErrors,
            });
        }
        return list;
    }

    private static List<UpsertedEntity<TKey>> RemapUpsertedEntities<TKey>(
        IReadOnlyList<UpsertedEntity<TKey>> entities, int[] survivorOriginalIndices)
        where TKey : notnull, IEquatable<TKey>
    {
        var list = new List<UpsertedEntity<TKey>>(entities.Count);
        foreach (var e in entities)
        {
            list.Add(new UpsertedEntity<TKey>
            {
                Id = e.Id,
                OriginalIndex = survivorOriginalIndices[e.OriginalIndex],
                Entity = e.Entity,
                Operation = e.Operation,
            });
        }
        return list;
    }

    private static List<UpsertFailure<TKey>> RemapUpsertFailures<TKey>(
        IReadOnlyList<UpsertFailure<TKey>> failures, int[] survivorOriginalIndices)
        where TKey : notnull, IEquatable<TKey>
    {
        var list = new List<UpsertFailure<TKey>>(failures.Count);
        foreach (var f in failures)
        {
            list.Add(new UpsertFailure<TKey>
            {
                EntityIndex = survivorOriginalIndices[f.EntityIndex],
                EntityId = f.EntityId,
                ErrorMessage = f.ErrorMessage,
                Reason = f.Reason,
                Exception = f.Exception,
                AttemptedOperation = f.AttemptedOperation,
                IsDefaultKey = f.IsDefaultKey,
                ValidationErrors = f.ValidationErrors,
            });
        }
        return list;
    }
}
