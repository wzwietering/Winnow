using Microsoft.EntityFrameworkCore;
using Winnow.Internal;
using Winnow.Internal.Accumulators;

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

    public void PrepareEntity(TEntity entity, int index, StrategyContext<TEntity, TKey> context)
    {
        var isInsert = context.HasDefaultKeyValue(entity);
        _accumulator.RecordOperationDecision(
            index, isInsert ? UpsertOperationType.Insert : UpsertOperationType.Update);

        context.Context.Entry(entity).State = isInsert ? EntityState.Added : EntityState.Modified;
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
