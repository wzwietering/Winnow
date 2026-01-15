using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Operations;

/// <summary>
/// Insert operation behavior for parent-only entity inserts.
/// Sets entities to Added state and tracks results by original index.
/// </summary>
internal class InsertOperation<TEntity> : IBatchInsertOperation<TEntity> where TEntity : class
{
    private readonly InsertBatchOptions _options;
    private readonly List<InsertedEntity> _insertedEntities = [];
    private readonly List<InsertBatchFailure> _failures = [];

    internal InsertOperation(InsertBatchOptions options)
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
            context.ValidateNoPopulatedNavigationProperties(entity);
        }
    }

    public void PrepareEntity(TEntity entity, int index, BatchStrategyContext<TEntity> context)
    {
        context.Context.Entry(entity).State = EntityState.Added;
    }

    public void RecordSuccess(TEntity entity, int index, BatchStrategyContext<TEntity> context)
    {
        var entityId = context.GetEntityId(entity);
        _insertedEntities.Add(new InsertedEntity
        {
            Id = entityId,
            OriginalIndex = index,
            Entity = entity
        });
    }

    public void RecordFailure(TEntity entity, int index, Exception ex, BatchStrategyContext<TEntity> context)
    {
        var failure = context.CreateInsertBatchFailure(index, ex);
        _failures.Add(failure);
    }

    public void CleanupEntity(TEntity entity, BatchStrategyContext<TEntity> context)
    {
        context.Context.Entry(entity).State = EntityState.Detached;
    }

    public InsertBatchResult CreateResult()
    {
        return new InsertBatchResult
        {
            InsertedEntities = _insertedEntities,
            Failures = _failures
        };
    }
}
