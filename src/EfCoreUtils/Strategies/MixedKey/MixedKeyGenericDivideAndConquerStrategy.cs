using EfCoreUtils.MixedKey;

namespace EfCoreUtils.Strategies.MixedKey;

/// <summary>
/// Generic divide-and-conquer batch processing strategy for mixed key types.
/// Attempts batch processing first, splits on failure for isolation.
/// Balances efficiency with failure isolation.
/// </summary>
internal class MixedKeyGenericDivideAndConquerStrategy<TEntity>
    where TEntity : class
{
    internal MixedKeyBatchResult Execute(
        List<TEntity> entities,
        MixedKeyBatchStrategyContext<TEntity> context,
        IMixedKeyBatchOperation<TEntity> operation)
    {
        operation.ValidateAll(entities, context);
        context.DetachAllEntities(entities);

        ProcessBatch(entities, context, operation);

        return operation.CreateResult();
    }

    internal MixedKeyInsertBatchResult ExecuteInsert(
        List<TEntity> entities,
        MixedKeyBatchStrategyContext<TEntity> context,
        IMixedKeyBatchInsertOperation<TEntity> operation)
    {
        operation.ValidateAll(entities, context);
        context.DetachAllEntities(entities);

        var indexedEntities = entities.Select((e, i) => (Entity: e, Index: i)).ToList();
        ProcessInsertBatch(indexedEntities, context, operation);

        return operation.CreateResult();
    }

    private void ProcessBatch(
        List<TEntity> entities,
        MixedKeyBatchStrategyContext<TEntity> context,
        IMixedKeyBatchOperation<TEntity> operation)
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

    private void ProcessInsertBatch(
        List<(TEntity Entity, int Index)> indexedEntities,
        MixedKeyBatchStrategyContext<TEntity> context,
        IMixedKeyBatchInsertOperation<TEntity> operation)
    {
        if (indexedEntities.Count == 0)
        {
            return;
        }

        if (indexedEntities.Count == 1)
        {
            var (entity, index) = indexedEntities[0];
            ProcessSingleInsert(entity, index, context, operation);
            return;
        }

        if (TryBatchInsert(indexedEntities, context, operation))
        {
            return;
        }

        SplitAndRecurseInsert(indexedEntities, context, operation);
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

    private static bool TryBatchProcess(
        List<TEntity> entities,
        MixedKeyBatchStrategyContext<TEntity> context,
        IMixedKeyBatchOperation<TEntity> operation)
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

    private static bool TryBatchInsert(
        List<(TEntity Entity, int Index)> indexedEntities,
        MixedKeyBatchStrategyContext<TEntity> context,
        IMixedKeyBatchInsertOperation<TEntity> operation)
    {
        try
        {
            foreach (var (entity, index) in indexedEntities)
            {
                operation.PrepareEntity(entity, index, context);
            }

            context.Context.SaveChanges();
            context.IncrementRoundTrip();

            RecordAllInsertSuccesses(indexedEntities, context, operation);
            return true;
        }
        catch
        {
            context.IncrementRoundTrip();
            CleanupAllInsertEntities(indexedEntities, context, operation);
            return false;
        }
    }

    private static void RecordAllSuccesses(
        List<TEntity> entities,
        MixedKeyBatchStrategyContext<TEntity> context,
        IMixedKeyBatchOperation<TEntity> operation)
    {
        foreach (var entity in entities)
        {
            operation.RecordSuccess(entity, context);
            operation.CleanupEntity(entity, context);
        }
    }

    private static void RecordAllInsertSuccesses(
        List<(TEntity Entity, int Index)> indexedEntities,
        MixedKeyBatchStrategyContext<TEntity> context,
        IMixedKeyBatchInsertOperation<TEntity> operation)
    {
        foreach (var (entity, index) in indexedEntities)
        {
            operation.RecordSuccess(entity, index, context);
            operation.CleanupEntity(entity, context);
        }
    }

    private static void CleanupAllEntities(
        List<TEntity> entities,
        MixedKeyBatchStrategyContext<TEntity> context,
        IMixedKeyBatchOperation<TEntity> operation)
    {
        foreach (var entity in entities)
        {
            operation.CleanupEntity(entity, context);
        }
    }

    private static void CleanupAllInsertEntities(
        List<(TEntity Entity, int Index)> indexedEntities,
        MixedKeyBatchStrategyContext<TEntity> context,
        IMixedKeyBatchInsertOperation<TEntity> operation)
    {
        foreach (var (entity, _) in indexedEntities)
        {
            operation.CleanupEntity(entity, context);
        }
    }

    private void SplitAndRecurse(
        List<TEntity> entities,
        MixedKeyBatchStrategyContext<TEntity> context,
        IMixedKeyBatchOperation<TEntity> operation)
    {
        var midpoint = entities.Count / 2;
        var firstHalf = entities.Take(midpoint).ToList();
        var secondHalf = entities.Skip(midpoint).ToList();

        ProcessBatch(firstHalf, context, operation);
        ProcessBatch(secondHalf, context, operation);
    }

    private void SplitAndRecurseInsert(
        List<(TEntity Entity, int Index)> indexedEntities,
        MixedKeyBatchStrategyContext<TEntity> context,
        IMixedKeyBatchInsertOperation<TEntity> operation)
    {
        var midpoint = indexedEntities.Count / 2;
        var firstHalf = indexedEntities.Take(midpoint).ToList();
        var secondHalf = indexedEntities.Skip(midpoint).ToList();

        ProcessInsertBatch(firstHalf, context, operation);
        ProcessInsertBatch(secondHalf, context, operation);
    }
}
