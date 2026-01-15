namespace EfCoreUtils.Strategies;

/// <summary>
/// Generic divide-and-conquer batch processing strategy.
/// Attempts batch processing first, splits on failure for isolation.
/// Balances efficiency with failure isolation.
/// </summary>
internal class GenericDivideAndConquerStrategy<TEntity> where TEntity : class
{
    internal BatchResult Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        IBatchOperation<TEntity> operation)
    {
        operation.ValidateAll(entities, context);
        context.DetachAllEntities(entities);

        ProcessBatch(entities, context, operation);

        return operation.CreateResult();
    }

    private void ProcessBatch(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        IBatchOperation<TEntity> operation)
    {
        if (entities.Count == 0)
        {
            return;
        }

        if (entities.Count == 1)
        {
            ProcessSingleEntity(entities[0], context, operation);
            return;
        }

        if (TryBatchProcess(entities, context, operation))
        {
            return;
        }

        SplitAndRecurse(entities, context, operation);
    }

    private static void ProcessSingleEntity(
        TEntity entity,
        BatchStrategyContext<TEntity> context,
        IBatchOperation<TEntity> operation)
    {
        try
        {
            operation.PrepareEntity(entity, context);
            context.Context.SaveChanges();
            context.IncrementRoundTrip();
            operation.RecordSuccess(entity, context);
        }
        catch (Exception ex)
        {
            context.IncrementRoundTrip();
            operation.RecordFailure(entity, ex, context);
        }
        finally
        {
            operation.CleanupEntity(entity, context);
        }
    }

    private static bool TryBatchProcess(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        IBatchOperation<TEntity> operation)
    {
        try
        {
            foreach (var entity in entities)
            {
                operation.PrepareEntity(entity, context);
            }

            context.Context.SaveChanges();
            context.IncrementRoundTrip();

            RecordAllSuccesses(entities, context, operation);
            return true;
        }
        catch
        {
            context.IncrementRoundTrip();
            CleanupAllEntities(entities, context, operation);
            return false;
        }
    }

    private static void RecordAllSuccesses(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        IBatchOperation<TEntity> operation)
    {
        foreach (var entity in entities)
        {
            operation.RecordSuccess(entity, context);
            operation.CleanupEntity(entity, context);
        }
    }

    private static void CleanupAllEntities(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        IBatchOperation<TEntity> operation)
    {
        foreach (var entity in entities)
        {
            operation.CleanupEntity(entity, context);
        }
    }

    private void SplitAndRecurse(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        IBatchOperation<TEntity> operation)
    {
        var midpoint = entities.Count / 2;
        var firstHalf = entities.Take(midpoint).ToList();
        var secondHalf = entities.Skip(midpoint).ToList();

        ProcessBatch(firstHalf, context, operation);
        ProcessBatch(secondHalf, context, operation);
    }
}
