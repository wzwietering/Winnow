using EfCoreUtils.Internal;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Operations;

/// <summary>
/// Upsert operation behavior for parent-only entities.
/// Routes entities to INSERT or UPDATE based on key value detection.
/// </summary>
/// <remarks>
/// <para><strong>Race Condition Warning:</strong></para>
/// <para>
/// This operation has a potential race condition between key detection and SaveChanges.
/// If another process inserts a row with the same key between these steps, the operation
/// may fail with a conflict error (classified as <see cref="FailureReason.Conflict"/>).
/// </para>
/// <para><strong>Mitigation strategies:</strong></para>
/// <list type="bullet">
/// <item>Implement retry logic for failed operations</item>
/// <item>Use database-level MERGE/INSERT ON CONFLICT for high-concurrency scenarios</item>
/// <item>Add optimistic concurrency tokens to detect conflicts</item>
/// </list>
/// </remarks>
internal class UpsertOperation<TEntity, TKey> : IBatchUpsertOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly UpsertBatchOptions _options;
    private readonly List<UpsertedEntity<TKey>> _insertedEntities = [];
    private readonly List<UpsertedEntity<TKey>> _updatedEntities = [];
    private readonly List<UpsertBatchFailure<TKey>> _failures = [];
    private readonly Dictionary<int, UpsertOperationType> _operationDecisions = [];

    internal UpsertOperation(UpsertBatchOptions options) => _options = options;

    public void ValidateAll(List<TEntity> entities, BatchStrategyContext<TEntity, TKey> context,
        CancellationToken cancellationToken = default)
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

    public void PrepareEntity(TEntity entity, int index, BatchStrategyContext<TEntity, TKey> context)
    {
        var isInsert = context.HasDefaultKeyValue(entity);
        _operationDecisions[index] = isInsert ? UpsertOperationType.Insert : UpsertOperationType.Update;

        context.Context.Entry(entity).State = isInsert ? EntityState.Added : EntityState.Modified;
    }

    public void RecordSuccess(TEntity entity, int index, BatchStrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);
        var operation = _operationDecisions.GetValueOrDefault(index, UpsertOperationType.Insert);

        var upsertedEntity = new UpsertedEntity<TKey>
        {
            Id = entityId,
            OriginalIndex = index,
            Entity = entity,
            Operation = operation
        };

        if (operation == UpsertOperationType.Insert)
        {
            _insertedEntities.Add(upsertedEntity);
        }
        else
        {
            _updatedEntities.Add(upsertedEntity);
        }
    }

    public void RecordFailure(TEntity entity, int index, Exception ex, BatchStrategyContext<TEntity, TKey> context)
    {
        var operation = _operationDecisions.GetValueOrDefault(index, UpsertOperationType.Insert);
        TKey? entityId = default;

        if (operation == UpsertOperationType.Update)
        {
            entityId = context.GetEntityId(entity);
        }

        var failure = new UpsertBatchFailure<TKey>
        {
            EntityIndex = index,
            EntityId = entityId,
            ErrorMessage = $"Upsert ({operation}) failed: {ex.Message}",
            Reason = FailureClassifier.Classify(ex),
            Exception = ex,
            AttemptedOperation = operation,
            IsDefaultKey = operation == UpsertOperationType.Insert
        };
        _failures.Add(failure);
    }

    public void CleanupEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context) =>
        context.Context.Entry(entity).State = EntityState.Detached;

    public UpsertBatchResult<TKey> CreateResult(bool wasCancelled = false) => new()
    {
        InsertedEntities = _insertedEntities,
        UpdatedEntities = _updatedEntities,
        Failures = _failures,
        WasCancelled = wasCancelled
    };

    public bool WasInsertAttempt(int index) =>
        _operationDecisions.GetValueOrDefault(index, UpsertOperationType.Insert) == UpsertOperationType.Insert;

    public DuplicateKeyStrategy DuplicateKeyStrategy => _options.DuplicateKeyStrategy;

    public void RecordSuccessAsUpdate(TEntity entity, int index, BatchStrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);
        _operationDecisions[index] = UpsertOperationType.Update;

        var upsertedEntity = new UpsertedEntity<TKey>
        {
            Id = entityId,
            OriginalIndex = index,
            Entity = entity,
            Operation = UpsertOperationType.Update
        };
        _updatedEntities.Add(upsertedEntity);
    }
}
