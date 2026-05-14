using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Winnow.Internal;
using Winnow.Internal.Accumulators;
using Winnow.Internal.Services;

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
internal class UpsertOperation<TEntity, TKey> : IUpsertOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly UpsertOptions _options;
    private readonly UpsertAccumulator<TKey> _accumulator;
    private MatchByResolution<TEntity>? _resolution;

    internal UpsertOperation(UpsertOptions options, UpsertAccumulator<TKey> accumulator)
    {
        _options = options;
        _accumulator = accumulator;
    }

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

    public void ResolveBatch(List<TEntity> entities, StrategyContext<TEntity, TKey> context)
    {
        if (_options.MatchBy is null) return;
        var prepared = PrepareMatchBatch(entities, context);
        var service = new MatchExpressionQueryService(context.Context);
        var existing = service.QueryExisting<TEntity>(prepared.Plan.Properties, prepared.Values);
        _resolution = BuildResolution(prepared, context.Context, existing);
    }

    public async Task ResolveBatchAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        CancellationToken cancellationToken)
    {
        if (_options.MatchBy is null) return;
        var prepared = PrepareMatchBatch(entities, context);
        var service = new MatchExpressionQueryService(context.Context);
        var existing = await service.QueryExistingAsync<TEntity>(
            prepared.Plan.Properties, prepared.Values, cancellationToken);
        _resolution = BuildResolution(prepared, context.Context, existing);
    }

    private PreparedMatchBatch PrepareMatchBatch(
        List<TEntity> entities, StrategyContext<TEntity, TKey> context)
    {
        var entityType = context.Context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException(
                $"Entity type {typeof(TEntity).Name} is not part of the DbContext model.");
        var plan = MatchExpressionParser.Parse<TEntity>(_options.MatchBy!, entityType);
        var values = ExtractMatchValues(entities, plan);
        RejectDuplicateMatchKeys(values);
        return new PreparedMatchBatch(plan, values, entityType);
    }

    private static MatchByResolution<TEntity> BuildResolution(
        PreparedMatchBatch prepared, DbContext dbContext, Dictionary<MatchKey, TEntity> existing) =>
        new(prepared.Plan,
            prepared.Values,
            dbContext,
            CollectConcurrencyTokens(prepared.EntityType),
            existing);

    private sealed record PreparedMatchBatch(
        MatchExpressionPlan<TEntity> Plan, object?[][] Values, IEntityType EntityType);

    private static IReadOnlyList<IProperty> CollectConcurrencyTokens(IEntityType entityType)
    {
        var result = new List<IProperty>();
        foreach (var property in entityType.GetProperties())
        {
            if (property.IsConcurrencyToken && !property.IsPrimaryKey())
            {
                result.Add(property);
            }
        }
        return result;
    }

    private static object?[][] ExtractMatchValues(
        List<TEntity> entities, MatchExpressionPlan<TEntity> plan)
    {
        var values = new object?[entities.Count][];
        for (var i = 0; i < entities.Count; i++)
        {
            values[i] = plan.Extractor(entities[i]);
        }
        return values;
    }

    private static void RejectDuplicateMatchKeys(object?[][] values)
    {
        var seen = new Dictionary<MatchKey, int>();
        for (var i = 0; i < values.Length; i++)
        {
            if (MatchKey.ContainsNull(values[i])) continue;
            var key = new MatchKey(values[i]);
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
        if (_resolution is null)
        {
            return context.HasDefaultKeyValue(entity);
        }

        var values = _resolution.EntityMatchValues[index];
        if (MatchKey.ContainsNull(values))
        {
            return true;
        }

        if (!_resolution.ExistingByMatchKey.TryGetValue(new MatchKey(values), out var existing))
        {
            return true;
        }

        ApplyExistingRowOnto(entity, existing, context);
        return false;
    }

    private void ApplyExistingRowOnto(TEntity input, TEntity existing, StrategyContext<TEntity, TKey> context)
    {
        var pk = context.GetEntityIdFromInstance(existing);
        context.SetEntityId(input, pk);
        CopyConcurrencyTokensFromExisting(input, existing);
    }

    private void CopyConcurrencyTokensFromExisting(TEntity input, TEntity existing)
    {
        if (_resolution is null) return;
        foreach (var token in _resolution.ConcurrencyTokens)
        {
            var propertyInfo = token.PropertyInfo
                ?? throw new InvalidOperationException(
                    $"Concurrency token '{token.Name}' is a shadow property; MatchBy requires CLR properties for concurrency tokens.");
            propertyInfo.SetValue(input, propertyInfo.GetValue(existing));
        }
    }

    public bool TryRefreshFromMatchBy(TEntity entity, StrategyContext<TEntity, TKey> context)
    {
        if (!TryPrepareRefresh(entity, out var values)) return false;

        var service = new MatchExpressionQueryService(_resolution!.DbContext);
        var dict = service.QueryExisting<TEntity>(_resolution.Plan.Properties, new[] { values });
        return TryApplyRefreshResult(entity, values, dict, context);
    }

    public async Task<bool> TryRefreshFromMatchByAsync(
        TEntity entity, StrategyContext<TEntity, TKey> context, CancellationToken cancellationToken)
    {
        if (!TryPrepareRefresh(entity, out var values)) return false;

        var service = new MatchExpressionQueryService(_resolution!.DbContext);
        var dict = await service.QueryExistingAsync<TEntity>(
            _resolution.Plan.Properties, new[] { values }, cancellationToken);
        return TryApplyRefreshResult(entity, values, dict, context);
    }

    private bool TryPrepareRefresh(TEntity entity, out object?[] values)
    {
        values = Array.Empty<object?>();
        if (_resolution is null) return false;
        values = _resolution.Plan.Extractor(entity);
        return !MatchKey.ContainsNull(values);
    }

    private bool TryApplyRefreshResult(
        TEntity entity, object?[] values, Dictionary<MatchKey, TEntity> dict,
        StrategyContext<TEntity, TKey> context)
    {
        if (!dict.TryGetValue(new MatchKey(values), out var existing)) return false;
        ApplyExistingRowOnto(entity, existing, context);
        return true;
    }

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
