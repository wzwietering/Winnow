using EfCoreUtils.Internal;
using Microsoft.Extensions.Logging;

namespace EfCoreUtils.Strategies;

/// <summary>
/// Generic divide-and-conquer batch processing strategy.
/// Attempts batch processing first, splits on failure for isolation.
/// Balances efficiency with failure isolation.
/// </summary>
internal class GenericDivideAndConquerStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    // === ASYNC METHODS ===

    internal async Task<BatchResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        operation.ValidateAll(entities, context);
        context.DetachAllEntities(entities);

        var wasCancelled = await ProcessBatchAsync(entities, context, operation, cancellationToken);

        return operation.CreateResult(wasCancelled);
    }

    internal async Task<InsertBatchResult<TKey>> ExecuteInsertAsync(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchInsertOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        operation.ValidateAll(entities, context);
        context.DetachAllEntities(entities);

        var indexedEntities = entities.Select((e, i) => (Entity: e, Index: i)).ToList();
        var wasCancelled = await ProcessInsertBatchAsync(indexedEntities, context, operation, cancellationToken);

        return operation.CreateResult(wasCancelled);
    }

    internal async Task<UpsertBatchResult<TKey>> ExecuteUpsertAsync(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchUpsertOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        operation.ValidateAll(entities, context, cancellationToken);
        context.DetachAllEntities(entities);

        var indexedEntities = entities.Select((e, i) => (Entity: e, Index: i)).ToList();
        var wasCancelled = await ProcessUpsertBatchAsync(indexedEntities, context, operation, cancellationToken);

        return operation.CreateResult(wasCancelled);
    }

    private async Task<bool> ProcessBatchAsync(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        if (entities.Count == 0)
        {
            return false;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return true;
        }

        if (entities.Count == 1)
        {
            return await ProcessSingleEntityAsync(entities[0], context, operation, cancellationToken);
        }

        if (await TryBatchProcessAsync(entities, context, operation, cancellationToken))
        {
            return false;
        }

        return await SplitAndRecurseAsync(entities, context, operation, cancellationToken);
    }

    private async Task<bool> ProcessInsertBatchAsync(
        List<(TEntity Entity, int Index)> indexedEntities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchInsertOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        if (indexedEntities.Count == 0)
        {
            return false;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return true;
        }

        if (indexedEntities.Count == 1)
        {
            var (entity, index) = indexedEntities[0];
            return await ProcessSingleInsertAsync(entity, index, context, operation, cancellationToken);
        }

        if (await TryBatchInsertAsync(indexedEntities, context, operation, cancellationToken))
        {
            return false;
        }

        return await SplitAndRecurseInsertAsync(indexedEntities, context, operation, cancellationToken);
    }

    private async Task<bool> ProcessUpsertBatchAsync(
        List<(TEntity Entity, int Index)> indexedEntities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchUpsertOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        if (indexedEntities.Count == 0)
        {
            return false;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return true;
        }

        if (indexedEntities.Count == 1)
        {
            var (entity, index) = indexedEntities[0];
            return await ProcessSingleUpsertAsync(entity, index, context, operation, cancellationToken);
        }

        if (await TryBatchUpsertAsync(indexedEntities, context, operation, cancellationToken))
        {
            return false;
        }

        return await SplitAndRecurseUpsertAsync(indexedEntities, context, operation, cancellationToken);
    }

    private static async Task<bool> ProcessSingleEntityAsync(
        TEntity entity,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            operation.PrepareEntity(entity, context);
            await SaveChangesRetryHandler.SaveWithRetryAsync(context.Context, context.RetryOptions, context.Logger, context.IncrementRetryCount, cancellationToken);
            context.IncrementRoundTrip();
            operation.RecordSuccess(entity, context);
            return false;
        }
        catch (OperationCanceledException)
        {
            context.IncrementRoundTrip();
            return true;
        }
        catch (Exception ex)
        {
            context.IncrementRoundTrip();
            operation.RecordFailure(entity, ex, context);
            return false;
        }
        finally
        {
            operation.CleanupEntity(entity, context);
        }
    }

    private static async Task<bool> ProcessSingleInsertAsync(
        TEntity entity,
        int index,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchInsertOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            operation.PrepareEntity(entity, index, context);
            await SaveChangesRetryHandler.SaveWithRetryAsync(context.Context, context.RetryOptions, context.Logger, context.IncrementRetryCount, cancellationToken);
            context.IncrementRoundTrip();
            operation.RecordSuccess(entity, index, context);
            return false;
        }
        catch (OperationCanceledException)
        {
            context.IncrementRoundTrip();
            return true;
        }
        catch (Exception ex)
        {
            context.IncrementRoundTrip();
            operation.RecordFailure(entity, index, ex, context);
            return false;
        }
        finally
        {
            operation.CleanupEntity(entity, context);
        }
    }

    private static async Task<bool> ProcessSingleUpsertAsync(
        TEntity entity,
        int index,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchUpsertOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            operation.PrepareEntity(entity, index, context);
            await SaveChangesRetryHandler.SaveWithRetryAsync(context.Context, context.RetryOptions, context.Logger, context.IncrementRetryCount, cancellationToken);
            context.IncrementRoundTrip();
            operation.RecordSuccess(entity, index, context);
            return false;
        }
        catch (OperationCanceledException)
        {
            context.IncrementRoundTrip();
            return true;
        }
        catch (Exception ex)
        {
            context.IncrementRoundTrip();

            if (DuplicateKeyHandler<TEntity, TKey>.ShouldHandle(ex, index, operation, out var strategy))
            {
                operation.CleanupEntity(entity, context);
                if (strategy == DuplicateKeyStrategy.RetryAsUpdate)
                {
                    return await DuplicateKeyHandler<TEntity, TKey>.RetryAsUpdateAsync(
                        entity, index, context, operation, cancellationToken);
                }
                return false;
            }

            operation.RecordFailure(entity, index, ex, context);
            return false;
        }
        finally
        {
            operation.CleanupEntity(entity, context);
        }
    }

    private static async Task<bool> TryBatchProcessAsync(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var entity in entities)
            {
                operation.PrepareEntity(entity, context);
            }

            await context.Context.SaveChangesAsync(cancellationToken);
            context.IncrementRoundTrip();

            RecordAllSuccesses(entities, context, operation);
            return true;
        }
        catch (OperationCanceledException)
        {
            context.IncrementRoundTrip();
            CleanupAllEntities(entities, context, operation);
            throw;
        }
        catch
        {
            context.IncrementRoundTrip();
            CleanupAllEntities(entities, context, operation);
            return false;
        }
    }

    private static async Task<bool> TryBatchInsertAsync(
        List<(TEntity Entity, int Index)> indexedEntities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchInsertOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var (entity, index) in indexedEntities)
            {
                operation.PrepareEntity(entity, index, context);
            }

            await context.Context.SaveChangesAsync(cancellationToken);
            context.IncrementRoundTrip();

            RecordAllInsertSuccesses(indexedEntities, context, operation);
            return true;
        }
        catch (OperationCanceledException)
        {
            context.IncrementRoundTrip();
            CleanupAllInsertEntities(indexedEntities, context, operation);
            throw;
        }
        catch
        {
            context.IncrementRoundTrip();
            CleanupAllInsertEntities(indexedEntities, context, operation);
            return false;
        }
    }

    private static async Task<bool> TryBatchUpsertAsync(
        List<(TEntity Entity, int Index)> indexedEntities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchUpsertOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var (entity, index) in indexedEntities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                operation.PrepareEntity(entity, index, context);
            }

            await context.Context.SaveChangesAsync(cancellationToken);
            context.IncrementRoundTrip();

            RecordAllUpsertSuccesses(indexedEntities, context, operation);
            return true;
        }
        catch (OperationCanceledException)
        {
            context.IncrementRoundTrip();
            CleanupAllUpsertEntities(indexedEntities, context, operation);
            throw;
        }
        catch
        {
            context.IncrementRoundTrip();
            CleanupAllUpsertEntities(indexedEntities, context, operation);
            return false;
        }
    }

    private async Task<bool> SplitAndRecurseAsync(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        var midpoint = entities.Count / 2;
        var firstHalf = entities.Take(midpoint).ToList();
        var secondHalf = entities.Skip(midpoint).ToList();
        BatchLogger.LogDivideAndConquerSplit(context.Logger, entities.Count, firstHalf.Count, secondHalf.Count);

        var firstCancelled = await ProcessBatchAsync(firstHalf, context, operation, cancellationToken);
        if (firstCancelled)
        {
            return true;
        }

        return await ProcessBatchAsync(secondHalf, context, operation, cancellationToken);
    }

    private async Task<bool> SplitAndRecurseInsertAsync(
        List<(TEntity Entity, int Index)> indexedEntities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchInsertOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        var midpoint = indexedEntities.Count / 2;
        var firstHalf = indexedEntities.Take(midpoint).ToList();
        var secondHalf = indexedEntities.Skip(midpoint).ToList();
        BatchLogger.LogDivideAndConquerSplit(context.Logger, indexedEntities.Count, firstHalf.Count, secondHalf.Count);

        var firstCancelled = await ProcessInsertBatchAsync(firstHalf, context, operation, cancellationToken);
        if (firstCancelled)
        {
            return true;
        }

        return await ProcessInsertBatchAsync(secondHalf, context, operation, cancellationToken);
    }

    private async Task<bool> SplitAndRecurseUpsertAsync(
        List<(TEntity Entity, int Index)> indexedEntities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchUpsertOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        var midpoint = indexedEntities.Count / 2;
        var firstHalf = indexedEntities.Take(midpoint).ToList();
        var secondHalf = indexedEntities.Skip(midpoint).ToList();
        BatchLogger.LogDivideAndConquerSplit(context.Logger, indexedEntities.Count, firstHalf.Count, secondHalf.Count);

        var firstCancelled = await ProcessUpsertBatchAsync(firstHalf, context, operation, cancellationToken);
        if (firstCancelled)
        {
            return true;
        }

        return await ProcessUpsertBatchAsync(secondHalf, context, operation, cancellationToken);
    }

    // === SYNC METHODS ===

    internal BatchResult<TKey> Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchOperation<TEntity, TKey> operation)
    {
        operation.ValidateAll(entities, context);
        context.DetachAllEntities(entities);

        ProcessBatch(entities, context, operation);

        return operation.CreateResult();
    }

    internal InsertBatchResult<TKey> ExecuteInsert(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchInsertOperation<TEntity, TKey> operation)
    {
        operation.ValidateAll(entities, context);
        context.DetachAllEntities(entities);

        var indexedEntities = entities.Select((e, i) => (Entity: e, Index: i)).ToList();
        ProcessInsertBatch(indexedEntities, context, operation);

        return operation.CreateResult();
    }

    internal UpsertBatchResult<TKey> ExecuteUpsert(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchUpsertOperation<TEntity, TKey> operation)
    {
        operation.ValidateAll(entities, context);
        context.DetachAllEntities(entities);

        var indexedEntities = entities.Select((e, i) => (Entity: e, Index: i)).ToList();
        ProcessUpsertBatch(indexedEntities, context, operation);

        return operation.CreateResult();
    }

    private void ProcessBatch(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchOperation<TEntity, TKey> operation)
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
        BatchStrategyContext<TEntity, TKey> context,
        IBatchInsertOperation<TEntity, TKey> operation)
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

    private void ProcessUpsertBatch(
        List<(TEntity Entity, int Index)> indexedEntities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchUpsertOperation<TEntity, TKey> operation)
    {
        if (indexedEntities.Count == 0)
        {
            return;
        }

        if (indexedEntities.Count == 1)
        {
            var (entity, index) = indexedEntities[0];
            ProcessSingleUpsert(entity, index, context, operation);
            return;
        }

        if (TryBatchUpsert(indexedEntities, context, operation))
        {
            return;
        }

        SplitAndRecurseUpsert(indexedEntities, context, operation);
    }

    private static void ProcessSingleEntity(
        TEntity entity,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchOperation<TEntity, TKey> operation)
    {
        try
        {
            operation.PrepareEntity(entity, context);
            SaveChangesRetryHandler.SaveWithRetry(context.Context, context.RetryOptions, context.Logger, context.IncrementRetryCount);
            context.IncrementRoundTrip();
            operation.RecordSuccess(entity, context);
        }
        catch (OperationCanceledException)
        {
            context.IncrementRoundTrip();
            throw;
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
        BatchStrategyContext<TEntity, TKey> context,
        IBatchInsertOperation<TEntity, TKey> operation)
    {
        try
        {
            operation.PrepareEntity(entity, index, context);
            SaveChangesRetryHandler.SaveWithRetry(context.Context, context.RetryOptions, context.Logger, context.IncrementRetryCount);
            context.IncrementRoundTrip();
            operation.RecordSuccess(entity, index, context);
        }
        catch (OperationCanceledException)
        {
            context.IncrementRoundTrip();
            throw;
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

    private static void ProcessSingleUpsert(
        TEntity entity,
        int index,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchUpsertOperation<TEntity, TKey> operation)
    {
        try
        {
            operation.PrepareEntity(entity, index, context);
            SaveChangesRetryHandler.SaveWithRetry(context.Context, context.RetryOptions, context.Logger, context.IncrementRetryCount);
            context.IncrementRoundTrip();
            operation.RecordSuccess(entity, index, context);
        }
        catch (OperationCanceledException)
        {
            context.IncrementRoundTrip();
            throw;
        }
        catch (Exception ex)
        {
            context.IncrementRoundTrip();

            if (DuplicateKeyHandler<TEntity, TKey>.ShouldHandle(ex, index, operation, out var strategy))
            {
                operation.CleanupEntity(entity, context);
                if (strategy == DuplicateKeyStrategy.RetryAsUpdate)
                {
                    DuplicateKeyHandler<TEntity, TKey>.RetryAsUpdate(entity, index, context, operation);
                }
                return;
            }

            operation.RecordFailure(entity, index, ex, context);
        }
        finally
        {
            operation.CleanupEntity(entity, context);
        }
    }

    private static bool TryBatchProcess(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchOperation<TEntity, TKey> operation)
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
        BatchStrategyContext<TEntity, TKey> context,
        IBatchInsertOperation<TEntity, TKey> operation)
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

    private static bool TryBatchUpsert(
        List<(TEntity Entity, int Index)> indexedEntities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchUpsertOperation<TEntity, TKey> operation)
    {
        try
        {
            foreach (var (entity, index) in indexedEntities)
            {
                operation.PrepareEntity(entity, index, context);
            }

            context.Context.SaveChanges();
            context.IncrementRoundTrip();

            RecordAllUpsertSuccesses(indexedEntities, context, operation);
            return true;
        }
        catch
        {
            context.IncrementRoundTrip();
            CleanupAllUpsertEntities(indexedEntities, context, operation);
            return false;
        }
    }

    private static void RecordAllSuccesses(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchOperation<TEntity, TKey> operation)
    {
        foreach (var entity in entities)
        {
            operation.RecordSuccess(entity, context);
            operation.CleanupEntity(entity, context);
        }
    }

    private static void RecordAllInsertSuccesses(
        List<(TEntity Entity, int Index)> indexedEntities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchInsertOperation<TEntity, TKey> operation)
    {
        foreach (var (entity, index) in indexedEntities)
        {
            operation.RecordSuccess(entity, index, context);
            operation.CleanupEntity(entity, context);
        }
    }

    private static void RecordAllUpsertSuccesses(
        List<(TEntity Entity, int Index)> indexedEntities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchUpsertOperation<TEntity, TKey> operation)
    {
        foreach (var (entity, index) in indexedEntities)
        {
            operation.RecordSuccess(entity, index, context);
            operation.CleanupEntity(entity, context);
        }
    }

    private static void CleanupAllEntities(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchOperation<TEntity, TKey> operation)
    {
        foreach (var entity in entities)
        {
            operation.CleanupEntity(entity, context);
        }
    }

    private static void CleanupAllInsertEntities(
        List<(TEntity Entity, int Index)> indexedEntities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchInsertOperation<TEntity, TKey> operation)
    {
        foreach (var (entity, _) in indexedEntities)
        {
            operation.CleanupEntity(entity, context);
        }
    }

    private static void CleanupAllUpsertEntities(
        List<(TEntity Entity, int Index)> indexedEntities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchUpsertOperation<TEntity, TKey> operation)
    {
        foreach (var (entity, _) in indexedEntities)
        {
            operation.CleanupEntity(entity, context);
        }
    }

    private void SplitAndRecurse(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchOperation<TEntity, TKey> operation)
    {
        var midpoint = entities.Count / 2;
        var firstHalf = entities.Take(midpoint).ToList();
        var secondHalf = entities.Skip(midpoint).ToList();
        BatchLogger.LogDivideAndConquerSplit(context.Logger, entities.Count, firstHalf.Count, secondHalf.Count);

        ProcessBatch(firstHalf, context, operation);
        ProcessBatch(secondHalf, context, operation);
    }

    private void SplitAndRecurseInsert(
        List<(TEntity Entity, int Index)> indexedEntities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchInsertOperation<TEntity, TKey> operation)
    {
        var midpoint = indexedEntities.Count / 2;
        var firstHalf = indexedEntities.Take(midpoint).ToList();
        var secondHalf = indexedEntities.Skip(midpoint).ToList();
        BatchLogger.LogDivideAndConquerSplit(context.Logger, indexedEntities.Count, firstHalf.Count, secondHalf.Count);

        ProcessInsertBatch(firstHalf, context, operation);
        ProcessInsertBatch(secondHalf, context, operation);
    }

    private void SplitAndRecurseUpsert(
        List<(TEntity Entity, int Index)> indexedEntities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchUpsertOperation<TEntity, TKey> operation)
    {
        var midpoint = indexedEntities.Count / 2;
        var firstHalf = indexedEntities.Take(midpoint).ToList();
        var secondHalf = indexedEntities.Skip(midpoint).ToList();
        BatchLogger.LogDivideAndConquerSplit(context.Logger, indexedEntities.Count, firstHalf.Count, secondHalf.Count);

        ProcessUpsertBatch(firstHalf, context, operation);
        ProcessUpsertBatch(secondHalf, context, operation);
    }
}
