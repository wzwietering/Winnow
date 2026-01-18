using EfCoreUtils.MixedKey;

namespace EfCoreUtils.Strategies.MixedKey;

/// <summary>
/// Generic one-by-one batch processing strategy for mixed key types.
/// Processes each entity individually with separate SaveChanges calls.
/// Maximum failure isolation but more database round trips.
/// </summary>
internal class MixedKeyGenericOneByOneStrategy<TEntity>
    where TEntity : class
{
    internal MixedKeyBatchResult Execute(
        List<TEntity> entities,
        MixedKeyBatchStrategyContext<TEntity> context,
        IMixedKeyBatchOperation<TEntity> operation)
    {
        operation.ValidateAll(entities, context);
        context.DetachAllEntities(entities);

        foreach (var entity in entities)
        {
            ProcessSingleEntity(entity, context, operation);
        }

        return operation.CreateResult();
    }

    internal MixedKeyInsertBatchResult ExecuteInsert(
        List<TEntity> entities,
        MixedKeyBatchStrategyContext<TEntity> context,
        IMixedKeyBatchInsertOperation<TEntity> operation)
    {
        operation.ValidateAll(entities, context);
        context.DetachAllEntities(entities);

        for (var i = 0; i < entities.Count; i++)
        {
            ProcessSingleInsert(entities[i], i, context, operation);
        }

        return operation.CreateResult();
    }

    private static void ProcessSingleEntity(
        TEntity entity,
        MixedKeyBatchStrategyContext<TEntity> context,
        IMixedKeyBatchOperation<TEntity> operation)
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

    private static void ProcessSingleInsert(
        TEntity entity,
        int index,
        MixedKeyBatchStrategyContext<TEntity> context,
        IMixedKeyBatchInsertOperation<TEntity> operation)
    {
        try
        {
            operation.PrepareEntity(entity, index, context);
            context.Context.SaveChanges();
            context.IncrementRoundTrip();
            operation.RecordSuccess(entity, index, context);
        }
        catch (Exception ex)
        {
            context.IncrementRoundTrip();
            operation.RecordFailure(entity, index, ex, context);
        }
        finally
        {
            operation.CleanupEntity(entity, context);
        }
    }
}
