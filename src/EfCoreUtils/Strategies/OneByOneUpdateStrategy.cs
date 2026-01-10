using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Strategies;

internal class OneByOneUpdateStrategy<TEntity> : IBatchUpdateStrategy<TEntity> where TEntity : class
{
    public BatchResult Execute(List<TEntity> entities, BatchStrategyContext<TEntity> context)
    {
        var successfulIds = new List<int>();
        var failures = new List<BatchFailure>();

        context.DetachAllEntities(entities);

        foreach (var entity in entities)
        {
            ProcessSingleEntity(entity, context, successfulIds, failures);
        }

        return OneByOneUpdateStrategy<TEntity>.CreateResult(successfulIds, failures);
    }

    private void ProcessSingleEntity(
        TEntity entity,
        BatchStrategyContext<TEntity> context,
        List<int> successfulIds,
        List<BatchFailure> failures)
    {
        try
        {
            context.Context.Entry(entity).State = EntityState.Modified;
            context.Context.SaveChanges();
            context.IncrementRoundTrip();

            var entityId = context.GetEntityId(entity);
            successfulIds.Add(entityId);

            context.Context.Entry(entity).State = EntityState.Detached;
        }
        catch (Exception ex)
        {
            OneByOneUpdateStrategy<TEntity>.HandleFailure(entity, ex, context, failures);
        }
    }

    private static void HandleFailure(
        TEntity entity,
        Exception ex,
        BatchStrategyContext<TEntity> context,
        List<BatchFailure> failures)
    {
        context.IncrementRoundTrip();
        var entityId = context.GetEntityId(entity);
        var failure = context.CreateBatchFailure(entityId, ex);
        failures.Add(failure);

        context.DetachEntity(entity);
    }

    private static BatchResult CreateResult(List<int> successfulIds, List<BatchFailure> failures)
    {
        return new BatchResult
        {
            SuccessfulIds = successfulIds,
            Failures = failures
        };
    }
}
