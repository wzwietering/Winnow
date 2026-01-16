using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Operations;

/// <summary>
/// Update operation behavior for parent-only entity updates.
/// Sets entities to Modified state and tracks results by entity ID.
/// </summary>
internal class UpdateOperation<TEntity, TKey> : IBatchOperation<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    private readonly BatchOptions _options;
    private readonly List<TKey> _successfulIds = [];
    private readonly List<BatchFailure<TKey>> _failures = [];

    internal UpdateOperation(BatchOptions options)
    {
        _options = options;
    }

    public void ValidateAll(List<TEntity> entities, BatchStrategyContext<TEntity, TKey> context)
    {
        if (!_options.ValidateNavigationProperties)
        {
            return;
        }

        foreach (var entity in entities)
        {
            context.ValidateNoModifiedNavigationProperties(entity);
        }
    }

    public void PrepareEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context)
    {
        context.Context.Entry(entity).State = EntityState.Modified;
    }

    public void RecordSuccess(TEntity entity, BatchStrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);
        _successfulIds.Add(entityId);
    }

    public void RecordFailure(TEntity entity, Exception ex, BatchStrategyContext<TEntity, TKey> context)
    {
        var entityId = context.GetEntityId(entity);
        var failure = context.CreateBatchFailure(entityId, ex);
        _failures.Add(failure);
    }

    public void CleanupEntity(TEntity entity, BatchStrategyContext<TEntity, TKey> context)
    {
        context.Context.Entry(entity).State = EntityState.Detached;
    }

    public BatchResult<TKey> CreateResult()
    {
        return new BatchResult<TKey>
        {
            SuccessfulIds = _successfulIds,
            Failures = _failures
        };
    }
}
