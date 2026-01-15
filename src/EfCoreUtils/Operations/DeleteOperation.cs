using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Operations;

/// <summary>
/// Delete operation behavior for parent-only entity deletes.
/// Sets entities to Deleted state and tracks results by entity ID.
/// </summary>
internal class DeleteOperation<TEntity> : IBatchOperation<TEntity> where TEntity : class
{
    private readonly DeleteBatchOptions _options;
    private readonly List<int> _successfulIds = [];
    private readonly List<BatchFailure> _failures = [];

    internal DeleteOperation(DeleteBatchOptions options)
    {
        _options = options;
    }

    public void ValidateAll(List<TEntity> entities, BatchStrategyContext<TEntity> context)
    {
        if (!_options.ValidateNavigationProperties)
        {
            return;
        }

        foreach (var entity in entities)
        {
            context.ValidateNoPopulatedNavigationPropertiesForDelete(entity);
        }
    }

    public void PrepareEntity(TEntity entity, BatchStrategyContext<TEntity> context)
    {
        context.AttachEntityAsDeleted(entity);
    }

    public void RecordSuccess(TEntity entity, BatchStrategyContext<TEntity> context)
    {
        var entityId = context.GetEntityId(entity);
        _successfulIds.Add(entityId);
    }

    public void RecordFailure(TEntity entity, Exception ex, BatchStrategyContext<TEntity> context)
    {
        var entityId = context.GetEntityId(entity);
        var failure = context.CreateBatchFailure(entityId, ex);
        _failures.Add(failure);
    }

    public void CleanupEntity(TEntity entity, BatchStrategyContext<TEntity> context)
    {
        context.Context.Entry(entity).State = EntityState.Detached;
    }

    public BatchResult CreateResult()
    {
        return new BatchResult
        {
            SuccessfulIds = _successfulIds,
            Failures = _failures
        };
    }
}
