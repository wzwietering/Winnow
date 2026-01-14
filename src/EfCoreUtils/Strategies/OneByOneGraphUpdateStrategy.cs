namespace EfCoreUtils.Strategies;

internal class OneByOneGraphUpdateStrategy<TEntity> : IBatchGraphUpdateStrategy<TEntity>
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

        // Capture original child IDs BEFORE detaching (required for orphan detection)
        context.CaptureAllOriginalChildIds(entities);

        // Validate orphans BEFORE detaching (if OrphanBehavior.Throw)
        ValidateAllOrphans(entities, context, options);

        context.DetachAllEntities(entities);

        foreach (var entity in entities)
        {
            ProcessSingleGraph(entity, context, options, successfulIds, failures, childIdsByParentId);
        }

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

    private void ProcessSingleGraph(
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

            // Handle orphaned children (delete if OrphanBehavior.Delete)
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
