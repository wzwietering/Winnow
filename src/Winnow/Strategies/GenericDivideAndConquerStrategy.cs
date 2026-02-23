using Winnow.Internal;
using Microsoft.Extensions.Logging;

namespace Winnow.Strategies;

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

    internal async Task<WinnowResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        IOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        operation.ValidateAll(entities, context);
        context.DetachAllEntities(entities);

        var wasCancelled = await ProcessBatchAsync(entities, context, operation, cancellationToken);

        return operation.CreateResult(wasCancelled);
    }

    internal async Task<InsertResult<TKey>> ExecuteInsertAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        IInsertOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        operation.ValidateAll(entities, context);
        context.DetachAllEntities(entities);

        var indexedEntities = entities.Select((e, i) => (Entity: e, Index: i)).ToList();
        var wasCancelled = await ProcessInsertAsync(indexedEntities, context, operation, cancellationToken);

        return operation.CreateResult(wasCancelled);
    }

    internal async Task<UpsertResult<TKey>> ExecuteUpsertAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        IUpsertOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        operation.ValidateAll(entities, context);
        context.DetachAllEntities(entities);

        var indexedEntities = entities.Select((e, i) => (Entity: e, Index: i)).ToList();
        var wasCancelled = await ProcessUpsertAsync(indexedEntities, context, operation, cancellationToken);

        return operation.CreateResult(wasCancelled);
    }

    private async Task<bool> ProcessBatchAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        IOperation<TEntity, TKey> operation,
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

    private async Task<bool> ProcessInsertAsync(
        List<(TEntity Entity, int Index)> indexedEntities,
        StrategyContext<TEntity, TKey> context,
        IInsertOperation<TEntity, TKey> operation,
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

    private async Task<bool> ProcessUpsertAsync(
        List<(TEntity Entity, int Index)> indexedEntities,
        StrategyContext<TEntity, TKey> context,
        IUpsertOperation<TEntity, TKey> operation,
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
        StrategyContext<TEntity, TKey> context,
        IOperation<TEntity, TKey> operation,
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
            WinnowLogger.LogEntityFailed(context.Logger, typeof(TEntity).Name,
                context.GetEntityIdString(entity), FailureClassifier.Classify(ex).ToString());
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
        StrategyContext<TEntity, TKey> context,
        IInsertOperation<TEntity, TKey> operation,
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
            WinnowLogger.LogEntityFailed(context.Logger, typeof(TEntity).Name,
                context.GetEntityIdString(entity), FailureClassifier.Classify(ex).ToString());
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
        StrategyContext<TEntity, TKey> context,
        IUpsertOperation<TEntity, TKey> operation,
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
                if (strategy == DuplicateKeyStrategy.RetryAsUpdate)
                {
                    return await DuplicateKeyHandler<TEntity, TKey>.RetryAsUpdateAsync(
                        entity, index, context, operation, cancellationToken);
                }
                return false;
            }

            WinnowLogger.LogEntityFailed(context.Logger, typeof(TEntity).Name,
                context.GetEntityIdString(entity), FailureClassifier.Classify(ex).ToString());
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
        StrategyContext<TEntity, TKey> context,
        IOperation<TEntity, TKey> operation,
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
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            context.IncrementRoundTrip();
            CleanupAllEntities(entities, context, operation);
            return false;
        }
    }

    private static async Task<bool> TryBatchInsertAsync(
        List<(TEntity Entity, int Index)> indexedEntities,
        StrategyContext<TEntity, TKey> context,
        IInsertOperation<TEntity, TKey> operation,
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
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            context.IncrementRoundTrip();
            CleanupAllInsertEntities(indexedEntities, context, operation);
            return false;
        }
    }

    private static async Task<bool> TryBatchUpsertAsync(
        List<(TEntity Entity, int Index)> indexedEntities,
        StrategyContext<TEntity, TKey> context,
        IUpsertOperation<TEntity, TKey> operation,
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
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            context.IncrementRoundTrip();
            CleanupAllUpsertEntities(indexedEntities, context, operation);
            return false;
        }
    }

    private async Task<bool> SplitAndRecurseAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        IOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        var midpoint = entities.Count / 2;
        var firstHalf = entities.Take(midpoint).ToList();
        var secondHalf = entities.Skip(midpoint).ToList();
        WinnowLogger.LogDivideAndConquerSplit(context.Logger, entities.Count, firstHalf.Count, secondHalf.Count);

        var firstCancelled = await ProcessBatchAsync(firstHalf, context, operation, cancellationToken);
        if (firstCancelled)
        {
            return true;
        }

        return await ProcessBatchAsync(secondHalf, context, operation, cancellationToken);
    }

    private async Task<bool> SplitAndRecurseInsertAsync(
        List<(TEntity Entity, int Index)> indexedEntities,
        StrategyContext<TEntity, TKey> context,
        IInsertOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        var midpoint = indexedEntities.Count / 2;
        var firstHalf = indexedEntities.Take(midpoint).ToList();
        var secondHalf = indexedEntities.Skip(midpoint).ToList();
        WinnowLogger.LogDivideAndConquerSplit(context.Logger, indexedEntities.Count, firstHalf.Count, secondHalf.Count);

        var firstCancelled = await ProcessInsertAsync(firstHalf, context, operation, cancellationToken);
        if (firstCancelled)
        {
            return true;
        }

        return await ProcessInsertAsync(secondHalf, context, operation, cancellationToken);
    }

    private async Task<bool> SplitAndRecurseUpsertAsync(
        List<(TEntity Entity, int Index)> indexedEntities,
        StrategyContext<TEntity, TKey> context,
        IUpsertOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        var midpoint = indexedEntities.Count / 2;
        var firstHalf = indexedEntities.Take(midpoint).ToList();
        var secondHalf = indexedEntities.Skip(midpoint).ToList();
        WinnowLogger.LogDivideAndConquerSplit(context.Logger, indexedEntities.Count, firstHalf.Count, secondHalf.Count);

        var firstCancelled = await ProcessUpsertAsync(firstHalf, context, operation, cancellationToken);
        if (firstCancelled)
        {
            return true;
        }

        return await ProcessUpsertAsync(secondHalf, context, operation, cancellationToken);
    }

    // === SYNC METHODS ===

    internal WinnowResult<TKey> Execute(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        IOperation<TEntity, TKey> operation)
    {
        operation.ValidateAll(entities, context);
        context.DetachAllEntities(entities);

        ProcessBatch(entities, context, operation);

        return operation.CreateResult();
    }

    internal InsertResult<TKey> ExecuteInsert(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        IInsertOperation<TEntity, TKey> operation)
    {
        operation.ValidateAll(entities, context);
        context.DetachAllEntities(entities);

        var indexedEntities = entities.Select((e, i) => (Entity: e, Index: i)).ToList();
        ProcessInsert(indexedEntities, context, operation);

        return operation.CreateResult();
    }

    internal UpsertResult<TKey> ExecuteUpsert(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        IUpsertOperation<TEntity, TKey> operation)
    {
        operation.ValidateAll(entities, context);
        context.DetachAllEntities(entities);

        var indexedEntities = entities.Select((e, i) => (Entity: e, Index: i)).ToList();
        ProcessUpsert(indexedEntities, context, operation);

        return operation.CreateResult();
    }

    private void ProcessBatch(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        IOperation<TEntity, TKey> operation)
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

    private void ProcessInsert(
        List<(TEntity Entity, int Index)> indexedEntities,
        StrategyContext<TEntity, TKey> context,
        IInsertOperation<TEntity, TKey> operation)
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

    private void ProcessUpsert(
        List<(TEntity Entity, int Index)> indexedEntities,
        StrategyContext<TEntity, TKey> context,
        IUpsertOperation<TEntity, TKey> operation)
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
        StrategyContext<TEntity, TKey> context,
        IOperation<TEntity, TKey> operation)
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
            WinnowLogger.LogEntityFailed(context.Logger, typeof(TEntity).Name,
                context.GetEntityIdString(entity), FailureClassifier.Classify(ex).ToString());
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
        StrategyContext<TEntity, TKey> context,
        IInsertOperation<TEntity, TKey> operation)
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
            WinnowLogger.LogEntityFailed(context.Logger, typeof(TEntity).Name,
                context.GetEntityIdString(entity), FailureClassifier.Classify(ex).ToString());
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
        StrategyContext<TEntity, TKey> context,
        IUpsertOperation<TEntity, TKey> operation)
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
                if (strategy == DuplicateKeyStrategy.RetryAsUpdate)
                {
                    DuplicateKeyHandler<TEntity, TKey>.RetryAsUpdate(entity, index, context, operation);
                }
                return;
            }

            WinnowLogger.LogEntityFailed(context.Logger, typeof(TEntity).Name,
                context.GetEntityIdString(entity), FailureClassifier.Classify(ex).ToString());
            operation.RecordFailure(entity, index, ex, context);
        }
        finally
        {
            operation.CleanupEntity(entity, context);
        }
    }

    private static bool TryBatchProcess(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        IOperation<TEntity, TKey> operation)
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
        catch (OperationCanceledException)
        {
            context.IncrementRoundTrip();
            CleanupAllEntities(entities, context, operation);
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            context.IncrementRoundTrip();
            CleanupAllEntities(entities, context, operation);
            return false;
        }
    }

    private static bool TryBatchInsert(
        List<(TEntity Entity, int Index)> indexedEntities,
        StrategyContext<TEntity, TKey> context,
        IInsertOperation<TEntity, TKey> operation)
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
        catch (OperationCanceledException)
        {
            context.IncrementRoundTrip();
            CleanupAllInsertEntities(indexedEntities, context, operation);
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            context.IncrementRoundTrip();
            CleanupAllInsertEntities(indexedEntities, context, operation);
            return false;
        }
    }

    private static bool TryBatchUpsert(
        List<(TEntity Entity, int Index)> indexedEntities,
        StrategyContext<TEntity, TKey> context,
        IUpsertOperation<TEntity, TKey> operation)
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
        catch (OperationCanceledException)
        {
            context.IncrementRoundTrip();
            CleanupAllUpsertEntities(indexedEntities, context, operation);
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            context.IncrementRoundTrip();
            CleanupAllUpsertEntities(indexedEntities, context, operation);
            return false;
        }
    }

    private static void RecordAllSuccesses(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        IOperation<TEntity, TKey> operation)
    {
        foreach (var entity in entities)
        {
            operation.RecordSuccess(entity, context);
            operation.CleanupEntity(entity, context);
        }
    }

    private static void RecordAllInsertSuccesses(
        List<(TEntity Entity, int Index)> indexedEntities,
        StrategyContext<TEntity, TKey> context,
        IInsertOperation<TEntity, TKey> operation)
    {
        foreach (var (entity, index) in indexedEntities)
        {
            operation.RecordSuccess(entity, index, context);
            operation.CleanupEntity(entity, context);
        }
    }

    private static void RecordAllUpsertSuccesses(
        List<(TEntity Entity, int Index)> indexedEntities,
        StrategyContext<TEntity, TKey> context,
        IUpsertOperation<TEntity, TKey> operation)
    {
        foreach (var (entity, index) in indexedEntities)
        {
            operation.RecordSuccess(entity, index, context);
            operation.CleanupEntity(entity, context);
        }
    }

    private static void CleanupAllEntities(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        IOperation<TEntity, TKey> operation)
    {
        foreach (var entity in entities)
        {
            operation.CleanupEntity(entity, context);
        }
    }

    private static void CleanupAllInsertEntities(
        List<(TEntity Entity, int Index)> indexedEntities,
        StrategyContext<TEntity, TKey> context,
        IInsertOperation<TEntity, TKey> operation)
    {
        foreach (var (entity, _) in indexedEntities)
        {
            operation.CleanupEntity(entity, context);
        }
    }

    private static void CleanupAllUpsertEntities(
        List<(TEntity Entity, int Index)> indexedEntities,
        StrategyContext<TEntity, TKey> context,
        IUpsertOperation<TEntity, TKey> operation)
    {
        foreach (var (entity, _) in indexedEntities)
        {
            operation.CleanupEntity(entity, context);
        }
    }

    private void SplitAndRecurse(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        IOperation<TEntity, TKey> operation)
    {
        var midpoint = entities.Count / 2;
        var firstHalf = entities.Take(midpoint).ToList();
        var secondHalf = entities.Skip(midpoint).ToList();
        WinnowLogger.LogDivideAndConquerSplit(context.Logger, entities.Count, firstHalf.Count, secondHalf.Count);

        ProcessBatch(firstHalf, context, operation);
        ProcessBatch(secondHalf, context, operation);
    }

    private void SplitAndRecurseInsert(
        List<(TEntity Entity, int Index)> indexedEntities,
        StrategyContext<TEntity, TKey> context,
        IInsertOperation<TEntity, TKey> operation)
    {
        var midpoint = indexedEntities.Count / 2;
        var firstHalf = indexedEntities.Take(midpoint).ToList();
        var secondHalf = indexedEntities.Skip(midpoint).ToList();
        WinnowLogger.LogDivideAndConquerSplit(context.Logger, indexedEntities.Count, firstHalf.Count, secondHalf.Count);

        ProcessInsert(firstHalf, context, operation);
        ProcessInsert(secondHalf, context, operation);
    }

    private void SplitAndRecurseUpsert(
        List<(TEntity Entity, int Index)> indexedEntities,
        StrategyContext<TEntity, TKey> context,
        IUpsertOperation<TEntity, TKey> operation)
    {
        var midpoint = indexedEntities.Count / 2;
        var firstHalf = indexedEntities.Take(midpoint).ToList();
        var secondHalf = indexedEntities.Skip(midpoint).ToList();
        WinnowLogger.LogDivideAndConquerSplit(context.Logger, indexedEntities.Count, firstHalf.Count, secondHalf.Count);

        ProcessUpsert(firstHalf, context, operation);
        ProcessUpsert(secondHalf, context, operation);
    }
}
