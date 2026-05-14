using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Winnow.Internal;
using Winnow.Internal.Accumulators;
using Winnow.Internal.Services;
using Winnow.Internal.Validation;

namespace Winnow.Operations;

/// <summary>
/// Upsert operation behavior for parent-only entities.
/// Routes entities to INSERT or UPDATE based on key value detection.
/// </summary>
/// <remarks>
/// <para><strong>Race Condition Warning:</strong></para>
/// <para>
/// This operation has a potential race condition between key detection and SaveChanges.
/// If another process inserts a row with the same key between these steps, the operation
/// may fail with a conflict error (classified as <see cref="FailureReason.DuplicateKey"/>).
/// </para>
/// <para><strong>Mitigation strategies:</strong></para>
/// <list type="bullet">
/// <item>Implement retry logic for failed operations</item>
/// <item>Use database-level MERGE/INSERT ON CONFLICT for high-concurrency scenarios</item>
/// <item>Add optimistic concurrency tokens to detect conflicts</item>
/// </list>
/// </remarks>
internal class UpsertOperation<TEntity, TKey> : IMatchByCapableOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly UpsertOptions _options;
    private readonly UpsertAccumulator<TKey> _accumulator;

    internal UpsertOperation(UpsertOptions options, UpsertAccumulator<TKey> accumulator)
    {
        _options = options;
        _accumulator = accumulator;
    }

    public ValidationOptions? Validation => _options.Validation;
    public UpsertAccumulator<TKey> Accumulator => _accumulator;

    public void ValidateAll(List<TEntity> entities, StrategyContext<TEntity, TKey> context)
    {
        if (!_options.ValidateNavigationProperties)
        {
            return;
        }

        foreach (var entity in entities)
        {
            context.ValidateNoPopulatedNavigationProperties(entity);
        }
    }

    public void ResolveBatch(
        List<TEntity> entities,
        int[]? originalIndices,
        int inputCount,
        StrategyContext<TEntity, TKey> context)
    {
        if (_options.MatchBy is null) return;
        _accumulator.MarkMatchByActive();
        var prepared = PrepareMatchBatch(entities, originalIndices, inputCount, context);
        WinnowLogger.LogMatchByPreSelect(
            context.Logger, typeof(TEntity).Name, entities.Count, prepared.Plan.Properties.Count);
        var existing = context.MatchByQueryService.QueryExisting<TEntity>(
            prepared.Plan.Properties, CompactValues(prepared.Values));
        context.MatchByResolution = BuildResolution(prepared, existing);
    }

    public async Task ResolveBatchAsync(
        List<TEntity> entities,
        int[]? originalIndices,
        int inputCount,
        StrategyContext<TEntity, TKey> context,
        CancellationToken cancellationToken)
    {
        if (_options.MatchBy is null) return;
        _accumulator.MarkMatchByActive();
        var prepared = PrepareMatchBatch(entities, originalIndices, inputCount, context);
        WinnowLogger.LogMatchByPreSelect(
            context.Logger, typeof(TEntity).Name, entities.Count, prepared.Plan.Properties.Count);
        var existing = await context.MatchByQueryService.QueryExistingAsync<TEntity>(
            prepared.Plan.Properties, CompactValues(prepared.Values), cancellationToken);
        context.MatchByResolution = BuildResolution(prepared, existing);
    }

    private PreparedMatchBatch PrepareMatchBatch(
        List<TEntity> entities,
        int[]? originalIndices,
        int inputCount,
        StrategyContext<TEntity, TKey> context)
    {
        var entityType = context.Context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException(
                $"Entity type {typeof(TEntity).Name} is not part of the DbContext model.");
        RejectGlobalQueryFilter(entityType);
        var plan = MatchExpressionParser.Parse<TEntity>(_options.MatchBy!.Expression, entityType);
        var values = ExtractMatchValues(entities, originalIndices, inputCount, plan);
        RejectDuplicateMatchKeys(values);
        return new PreparedMatchBatch(plan, values, entityType);
    }

    private static object?[][] CompactValues(object?[]?[] sparse)
    {
        var compact = new List<object?[]>(sparse.Length);
        foreach (var row in sparse)
        {
            if (row is not null) compact.Add(row);
        }
        return compact.ToArray();
    }

    private static void RejectGlobalQueryFilter(IEntityType entityType)
    {
        // AsNoTracking does not suppress global query filters; a soft-delete or
        // multi-tenant filter would cause the pre-SELECT to miss matching rows
        // and silently route entities to INSERT (producing duplicates). Refuse
        // to run rather than corrupt data quietly. Callers must either remove
        // the filter or upsert against a different entity type.
        if (!HasQueryFilter(entityType)) return;
        throw new InvalidOperationException(
            $"MatchBy refuses to run against {entityType.ClrType.Name} because the entity type " +
            $"has a global query filter (HasQueryFilter). The pre-SELECT would silently miss " +
            $"filtered rows and route existing entities to INSERT, producing duplicates. " +
            $"Mitigations: (1) remove the HasQueryFilter configuration for this entity type; " +
            $"(2) omit MatchBy and fall back to primary-key default-value routing; " +
            $"(3) wait for a future Winnow release that exposes an opt-in to ignore filters.");
    }

    private static bool HasQueryFilter(IEntityType entityType)
    {
#if NET10_0_OR_GREATER
        return entityType.GetDeclaredQueryFilters().Count > 0;
#else
        return entityType.GetQueryFilter() is not null;
#endif
    }

    private static MatchByResolution<TEntity> BuildResolution(
        PreparedMatchBatch prepared, Dictionary<MatchKey, TEntity> existing) =>
        new(prepared.Plan,
            prepared.Values,
            CollectConcurrencyTokens(prepared.EntityType),
            existing);

    private sealed record PreparedMatchBatch(
        MatchExpressionPlan<TEntity> Plan, object?[]?[] Values, IEntityType EntityType);

    private static IReadOnlyList<IProperty> CollectConcurrencyTokens(IEntityType entityType)
    {
        var result = new List<IProperty>();
        foreach (var property in entityType.GetProperties())
        {
            if (!property.IsConcurrencyToken || property.IsPrimaryKey()) continue;
            EnsureClrBackedToken(property, entityType);
            result.Add(property);
        }
        return result;
    }

    private static void EnsureClrBackedToken(IProperty property, IEntityType entityType)
    {
        // Shadow tokens cannot be copied via reflection because there is no CLR
        // backing property. Reject at ResolveBatch time so the caller fails before
        // any entity has been touched, rather than as a per-entity failure mid-batch.
        if (property.PropertyInfo is not null) return;
        throw new InvalidOperationException(
            $"Concurrency token '{property.Name}' on {entityType.ClrType.Name} is a shadow property; " +
            $"MatchBy requires CLR-backed concurrency tokens. Map '{property.Name}' as a public property " +
            $"or remove its IsConcurrencyToken configuration.");
    }

    private static object?[]?[] ExtractMatchValues(
        List<TEntity> entities,
        int[]? originalIndices,
        int inputCount,
        MatchExpressionPlan<TEntity> plan)
    {
        var values = new object?[]?[inputCount];
        for (var i = 0; i < entities.Count; i++)
        {
            var originalIndex = originalIndices is null ? i : originalIndices[i];
            values[originalIndex] = plan.Extractor(entities[i]);
        }
        return values;
    }

    private static void RejectDuplicateMatchKeys(object?[]?[] values)
    {
        var seen = new Dictionary<MatchKey, int>();
        for (var i = 0; i < values.Length; i++)
        {
            var row = values[i];
            if (row is null) continue;
            if (MatchKey.ContainsNull(row)) continue;
            var key = new MatchKey(row);
            if (seen.TryGetValue(key, out var firstIndex))
            {
                throw new InvalidOperationException(
                    $"MatchBy resolved duplicate values for entities at indices " +
                    $"{firstIndex} and {i}. Input batch must not contain duplicate match keys.");
            }
            seen[key] = i;
        }
    }

    public void PrepareEntity(TEntity entity, int index, StrategyContext<TEntity, TKey> context)
    {
        var isInsert = DecideInsertOrUpdate(entity, index, context);
        _accumulator.RecordOperationDecision(
            index, isInsert ? UpsertOperationType.Insert : UpsertOperationType.Update);

        context.Context.Entry(entity).State = isInsert ? EntityState.Added : EntityState.Modified;
    }

    private bool DecideInsertOrUpdate(TEntity entity, int index, StrategyContext<TEntity, TKey> context)
    {
        var resolution = context.MatchByResolution;
        if (resolution is null)
        {
            return context.HasDefaultKeyValue(entity);
        }

        // EntityMatchValues is sparse: pre-validation rejects leave null holes, but
        // rejected entities never reach this method, so the slot is populated by construction.
        var values = resolution.EntityMatchValues[index]!;
        if (MatchKey.ContainsNull(values))
        {
            _accumulator.RecordNullMatchKeyInsert();
            return true;
        }

        if (!resolution.ExistingByMatchKey.TryGetValue(new MatchKey(values), out var existing))
        {
            return true;
        }

        ApplyExistingRowOnto(entity, existing, context);
        return false;
    }

    private static void ApplyExistingRowOnto(TEntity input, TEntity existing, StrategyContext<TEntity, TKey> context)
    {
        var pk = context.GetEntityIdFromInstance(existing);
        context.SetEntityId(input, pk);
        CopyConcurrencyTokensFromExisting(input, existing, context);
    }

    private static void CopyConcurrencyTokensFromExisting(
        TEntity input, TEntity existing, StrategyContext<TEntity, TKey> context)
    {
        var resolution = context.MatchByResolution;
        if (resolution is null) return;
        foreach (var token in resolution.ConcurrencyTokens)
        {
            // PropertyInfo is guaranteed non-null: CollectConcurrencyTokens rejects shadow
            // tokens at ResolveBatch time.
            var propertyInfo = token.PropertyInfo!;
            propertyInfo.SetValue(input, propertyInfo.GetValue(existing));
        }
    }

    public MatchByRefreshOutcome TryRefreshFromMatchBy(TEntity entity, StrategyContext<TEntity, TKey> context)
    {
        var resolution = context.MatchByResolution;
        if (resolution is null) return MatchByRefreshOutcome.NotApplicable;

        var values = resolution.Plan.Extractor(entity);
        if (MatchKey.ContainsNull(values)) return MatchByRefreshOutcome.NotFound;

        var dict = context.MatchByQueryService.QueryExisting<TEntity>(
            resolution.Plan.Properties, new[] { values });
        return ApplyRefreshResult(entity, values, dict, context);
    }

    public async Task<MatchByRefreshOutcome> TryRefreshFromMatchByAsync(
        TEntity entity, StrategyContext<TEntity, TKey> context, CancellationToken cancellationToken)
    {
        var resolution = context.MatchByResolution;
        if (resolution is null) return MatchByRefreshOutcome.NotApplicable;

        var values = resolution.Plan.Extractor(entity);
        if (MatchKey.ContainsNull(values)) return MatchByRefreshOutcome.NotFound;

        var dict = await context.MatchByQueryService.QueryExistingAsync<TEntity>(
            resolution.Plan.Properties, new[] { values }, cancellationToken);
        return ApplyRefreshResult(entity, values, dict, context);
    }

    private static MatchByRefreshOutcome ApplyRefreshResult(
        TEntity entity, object?[] values, Dictionary<MatchKey, TEntity> dict,
        StrategyContext<TEntity, TKey> context)
    {
        if (!dict.TryGetValue(new MatchKey(values), out var existing)) return MatchByRefreshOutcome.NotFound;
        ApplyExistingRowOnto(entity, existing, context);
        return MatchByRefreshOutcome.Refreshed;
    }

    public void RecordBusinessKeyConflictLost(TEntity entity, int index, StrategyContext<TEntity, TKey> context) =>
        _accumulator.RecordFailure(
            index,
            entityId: default,
            "Upsert retry-as-update could not refresh from MatchBy: no row matched the configured match expression at retry time. " +
            "A concurrent process likely won the race (INSERT-then-DELETE between our save and our retry).",
            FailureReason.BusinessKeyConflictLost,
            exception: null,
            UpsertOperationType.Insert);

    public void RecordSuccess(TEntity entity, int index, StrategyContext<TEntity, TKey> context) =>
        _accumulator.RecordSuccess(context.GetEntityId(entity), index, entity);

    public void RecordFailure(TEntity entity, int index, Exception ex, StrategyContext<TEntity, TKey> context)
    {
        var operation = _accumulator.GetOperationDecision(index);
        TKey? entityId = operation == UpsertOperationType.Update ? context.GetEntityId(entity) : default;
        _accumulator.RecordFailure(
            index,
            entityId,
            $"Upsert ({operation}) failed: {ex.Message}",
            FailureClassifier.Classify(ex),
            ex,
            operation);
    }

    public void CleanupEntity(TEntity entity, StrategyContext<TEntity, TKey> context)
    {
        try
        {
            context.Context.Entry(entity).State = EntityState.Detached;
        }
        catch (ArgumentNullException)
        {
            // EF Core 8/9 throws when detaching entities with null keys (e.g., null string PKs).
            // EF Core 10+ handles this gracefully. Cleanup is best-effort, so swallow the error.
        }
    }

    public UpsertResult<TKey> CreateResult(bool wasCancelled = false) => _accumulator.Build(wasCancelled);

    public bool WasInsertAttempt(int index) => _accumulator.WasInsertAttempt(index);

    public DuplicateKeyStrategy DuplicateKeyStrategy => _options.DuplicateKeyStrategy;

    public void RecordSuccessAsUpdate(TEntity entity, int index, StrategyContext<TEntity, TKey> context) =>
        _accumulator.RecordSuccessAsUpdate(context.GetEntityId(entity), index, entity);
}
