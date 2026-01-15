namespace EfCoreUtils.Strategies;

/// <summary>
/// Generic one-by-one batch processing strategy.
/// Processes each entity individually with separate SaveChanges calls.
/// Maximum failure isolation but more database round trips.
/// </summary>
internal class GenericOneByOneStrategy<TEntity> where TEntity : class
{
    internal BatchResult Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity> context,
        IBatchOperation<TEntity> operation)
    {
        operation.ValidateAll(entities, context);
        context.DetachAllEntities(entities);

        foreach (var entity in entities)
        {
            ProcessSingleEntity(entity, context, operation);
        }

        return operation.CreateResult();
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
}
