namespace EfCoreUtils.Strategies;

internal class DivideAndConquerGraphUpdateStrategy<TEntity> : IBatchGraphUpdateStrategy<TEntity>
    where TEntity : class
{
    public BatchResult Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        GraphBatchOptions options)
    {
        var successfulIds = new List<int>();
        var failures = new List<BatchFailure>();
        var childIdsByParentId = new Dictionary<int, IReadOnlyList<int>>();

        context.CaptureAllOriginalChildIds(entities);
        ValidateAllOrphans(entities, context, options);
        context.DetachAllEntities(entities);

        ProcessBatch(entities, context, options, successfulIds, failures, childIdsByParentId);

        return CreateResult(successfulIds, failures, childIdsByParentId);
    }

    private static void ValidateAllOrphans(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        GraphBatchOptions options)
    {
        foreach (var entity in entities)
        {
            context.ValidateNoOrphanedChildren(entity, options);
        }
    }

    private void ProcessBatch(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        GraphBatchOptions options,
        List<int> successfulIds,
        List<BatchFailure> failures,
        Dictionary<int, IReadOnlyList<int>> childIdsByParentId)
    {
        if (entities.Count == 0)
        {
            return;
        }

        if (entities.Count == 1)
        {
            ProcessSingleGraph(entities[0], context, options, successfulIds, failures, childIdsByParentId);
            return;
        }

        if (TryBatchGraphUpdate(entities, context, options, successfulIds, childIdsByParentId))
        {
            return;
        }

        SplitAndRecurse(entities, context, options, successfulIds, failures, childIdsByParentId);
    }

    private static void ProcessSingleGraph(
        TEntity entity,
        BatchStrategyContext<TEntity> context,
        GraphBatchOptions options,
        List<int> successfulIds,
        List<BatchFailure> failures,
        Dictionary<int, IReadOnlyList<int>> childIdsByParentId)
    {
        var entityId = context.GetEntityId(entity);

        try
        {
            context.AttachEntityGraphAsModified(entity);
            context.HandleOrphanedChildren(entity, options);

            var childIds = context.GetChildIds(entity);
            context.Context.SaveChanges();
            context.IncrementRoundTrip();

            successfulIds.Add(entityId);
            childIdsByParentId[entityId] = childIds;
        }
        catch (Exception ex)
        {
            HandleGraphFailure(entityId, ex, context, failures);
        }
        finally
        {
            context.DetachEntityWithOrphans(entity);
        }
    }

    private static bool TryBatchGraphUpdate(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        GraphBatchOptions options,
        List<int> successfulIds,
        Dictionary<int, IReadOnlyList<int>> childIdsByParentId)
    {
        var tempChildIds = new Dictionary<int, IReadOnlyList<int>>();

        try
        {
            AttachAllGraphs(entities, context, options, tempChildIds);
            context.Context.SaveChanges();
            context.IncrementRoundTrip();

            RecordSuccessfulGraphs(entities, context, successfulIds, tempChildIds, childIdsByParentId);
            return true;
        }
        catch
        {
            context.IncrementRoundTrip();
            DetachAllGraphs(entities, context);
            return false;
        }
    }

    private static void AttachAllGraphs(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        GraphBatchOptions options,
        Dictionary<int, IReadOnlyList<int>> tempChildIds)
    {
        foreach (var entity in entities)
        {
            context.AttachEntityGraphAsModified(entity);
            context.HandleOrphanedChildren(entity, options);
            tempChildIds[context.GetEntityId(entity)] = context.GetChildIds(entity);
        }
    }

    private static void RecordSuccessfulGraphs(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        List<int> successfulIds,
        Dictionary<int, IReadOnlyList<int>> tempChildIds,
        Dictionary<int, IReadOnlyList<int>> childIdsByParentId)
    {
        foreach (var entity in entities)
        {
            var entityId = context.GetEntityId(entity);
            successfulIds.Add(entityId);
            childIdsByParentId[entityId] = tempChildIds[entityId];
            context.DetachEntityWithOrphans(entity);
        }
    }

    private void SplitAndRecurse(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        GraphBatchOptions options,
        List<int> successfulIds,
        List<BatchFailure> failures,
        Dictionary<int, IReadOnlyList<int>> childIdsByParentId)
    {
        var midpoint = entities.Count / 2;
        var firstHalf = entities.Take(midpoint).ToList();
        var secondHalf = entities.Skip(midpoint).ToList();

        ProcessBatch(firstHalf, context, options, successfulIds, failures, childIdsByParentId);
        ProcessBatch(secondHalf, context, options, successfulIds, failures, childIdsByParentId);
    }

    private static void HandleGraphFailure(
        int entityId,
        Exception ex,
        BatchStrategyContext<TEntity> context,
        List<BatchFailure> failures)
    {
        context.IncrementRoundTrip();

        var failure = new BatchFailure
        {
            EntityId = entityId,
            ErrorMessage = $"Graph update failed: {ex.Message}",
            Reason = ClassifyException(ex),
            Exception = ex
        };
        failures.Add(failure);
    }

    private static void DetachAllGraphs(List<TEntity> entities, BatchStrategyContext<TEntity> context)
    {
        foreach (var entity in entities)
        {
            context.DetachEntityWithOrphans(entity);
        }
    }

    private static FailureReason ClassifyException(Exception ex)
    {
        return ex switch
        {
            InvalidOperationException => FailureReason.ValidationError,
            Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => FailureReason.ConcurrencyConflict,
            Microsoft.EntityFrameworkCore.DbUpdateException => FailureReason.DatabaseConstraint,
            _ => FailureReason.UnknownError
        };
    }

    private static BatchResult CreateResult(
        List<int> successfulIds,
        List<BatchFailure> failures,
        Dictionary<int, IReadOnlyList<int>> childIdsByParentId)
    {
        return new BatchResult
        {
            SuccessfulIds = successfulIds,
            Failures = failures,
            ChildIdsByParentId = childIdsByParentId
        };
    }
}
