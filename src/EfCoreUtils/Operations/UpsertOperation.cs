using EfCoreUtils.Internal;
using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Operations;

/// <summary>
/// Upsert operation behavior for parent-only entities.
/// Routes entities to INSERT or UPDATE based on key value detection.
/// </summary>
internal class UpsertOperation<TEntity, TKey> : IBatchUpsertOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly UpsertBatchOptions _options;
    private readonly List<UpsertedEntity<TKey>> _insertedEntities = [];
    private readonly List<UpsertedEntity<TKey>> _updatedEntities = [];
    private readonly List<UpsertBatchFailure<TKey>> _failures = [];
    private readonly Dictionary<int, UpsertOperation> _operationDecisions = [];

    internal UpsertOperation(UpsertBatchOptions options) => _options = options;

    public void ValidateAll(List<TEntity> entities, BatchStrategyContext<TEntity, TKey> context)
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
        _operationDecisions[index] = isInsert ? UpsertOperation.Insert : UpsertOperation.Update;

        context.Context.Entry(entity).State = isInsert ? EntityState.Added : EntityState.Modified;
    }

    public void RecordSuccess(TEntity entity, int index, BatchStrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);
        var operation = _operationDecisions.GetValueOrDefault(index, UpsertOperation.Insert);

        var upsertedEntity = new UpsertedEntity<TKey>
        {
            Id = entityId,
            OriginalIndex = index,
            Entity = entity,
            Operation = operation
        };

        if (operation == UpsertOperation.Insert)
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
        var operation = _operationDecisions.GetValueOrDefault(index, UpsertOperation.Insert);
        TKey? entityId = default;

        if (operation == UpsertOperation.Update)
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
            AttemptedOperation = operation
        };
        _failures.Add(failure);
    }

    public void CleanupEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context) =>
        context.Context.Entry(entity).State = EntityState.Detached;

    public UpsertBatchResult<TKey> CreateResult() => new()
    {
        InsertedEntities = _insertedEntities,
        UpdatedEntities = _updatedEntities,
        Failures = _failures
    };
}
