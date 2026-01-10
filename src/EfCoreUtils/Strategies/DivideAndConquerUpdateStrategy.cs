using Microsoft.EntityFrameworkCore;

namespace EfCoreUtils.Strategies;

internal class DivideAndConquerUpdateStrategy<TEntity> : IBatchUpdateStrategy<TEntity> where TEntity : class
{
    public BatchResult Execute(List<TEntity> entities, BatchStrategyContext<TEntity> context)
    {
        var successfulIds = new List<int>();
        var failures = new List<BatchFailure>();

        context.DetachAllEntities(entities);
        ProcessBatch(entities, context, successfulIds, failures);

        return DivideAndConquerUpdateStrategy<TEntity>.CreateResult(successfulIds, failures);
    }

    private void ProcessBatch(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        List<int> successfulIds,
        List<BatchFailure> failures)
    {
        if (entities.Count == 0)
        {
            return;
        }

        if (entities.Count == 1)
        {
            DivideAndConquerUpdateStrategy<TEntity>.ProcessSingleEntity(entities[0], context, successfulIds, failures);
            return;
        }

        if (DivideAndConquerUpdateStrategy<TEntity>.TryBatchUpdate(entities, context, successfulIds))
        {
            return;
        }

        SplitAndRecurse(entities, context, successfulIds, failures);
    }

    private static void ProcessSingleEntity(
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
            DivideAndConquerUpdateStrategy<TEntity>.HandleFailure(entity, ex, context, failures);
        }
    }

    private static bool TryBatchUpdate(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        List<int> successfulIds)
    {
        try
        {
            foreach (var entity in entities)
            {
                context.Context.Entry(entity).State = EntityState.Modified;
            }

            context.Context.SaveChanges();
            context.IncrementRoundTrip();

            DivideAndConquerUpdateStrategy<TEntity>.RecordSuccessfulEntities(entities, context, successfulIds);
            return true;
        }
        catch
        {
            context.IncrementRoundTrip();
            DivideAndConquerUpdateStrategy<TEntity>.DetachAllEntities(entities, context);
            return false;
        }
    }

    private static void RecordSuccessfulEntities(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        List<int> successfulIds)
    {
        foreach (var entity in entities)
        {
            var entityId = context.GetEntityId(entity);
            successfulIds.Add(entityId);
            context.Context.Entry(entity).State = EntityState.Detached;
        }
    }

    private static void DetachAllEntities(List<TEntity> entities, BatchStrategyContext<TEntity> context)
    {
        foreach (var entity in entities)
        {
            context.DetachEntity(entity);
        }
    }

    private void SplitAndRecurse(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        List<int> successfulIds,
        List<BatchFailure> failures)
    {
        var midpoint = entities.Count / 2;
        var firstHalf = entities.Take(midpoint).ToList();
        var secondHalf = entities.Skip(midpoint).ToList();

        ProcessBatch(firstHalf, context, successfulIds, failures);
        ProcessBatch(secondHalf, context, successfulIds, failures);
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
