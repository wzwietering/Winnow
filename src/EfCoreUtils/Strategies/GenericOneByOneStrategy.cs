using EfCoreUtils.Internal;
using Microsoft.Extensions.Logging;

namespace EfCoreUtils.Strategies;

/// <summary>
/// Generic one-by-one batch processing strategy.
/// Processes each entity individually with separate SaveChanges calls.
/// Maximum failure isolation but more database round trips.
/// </summary>
internal class GenericOneByOneStrategy<TEntity, TKey>
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

        var wasCancelled = false;
        foreach (var entity in entities)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                wasCancelled = true;
                break;
            }

            await ProcessSingleEntityAsync(entity, context, operation, cancellationToken);
        }

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

        var wasCancelled = false;
        for (var i = 0; i < entities.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                wasCancelled = true;
                break;
            }

            await ProcessSingleInsertAsync(entities[i], i, context, operation, cancellationToken);
        }

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

        var wasCancelled = false;
        for (var i = 0; i < entities.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                wasCancelled = true;
                break;
            }

            await ProcessSingleUpsertAsync(entities[i], i, context, operation, cancellationToken);
        }

        return operation.CreateResult(wasCancelled);
    }

    private static async Task ProcessSingleEntityAsync(
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
        }
        catch (OperationCanceledException)
        {
            context.IncrementRoundTrip();
            throw;
        }
        catch (Exception ex)
        {
            context.IncrementRoundTrip();
            BatchLogger.LogEntityFailed(context.Logger, typeof(TEntity).Name,
                context.GetEntityIdString(entity), FailureClassifier.Classify(ex).ToString());
            operation.RecordFailure(entity, ex, context);
        }
        finally
        {
            operation.CleanupEntity(entity, context);
        }
    }

    private static async Task ProcessSingleInsertAsync(
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
        }
        catch (OperationCanceledException)
        {
            context.IncrementRoundTrip();
            throw;
        }
        catch (Exception ex)
        {
            context.IncrementRoundTrip();
            BatchLogger.LogEntityFailed(context.Logger, typeof(TEntity).Name,
                context.GetEntityIdString(entity), FailureClassifier.Classify(ex).ToString());
            operation.RecordFailure(entity, index, ex, context);
        }
        finally
        {
            operation.CleanupEntity(entity, context);
        }
    }

    private static async Task ProcessSingleUpsertAsync(
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
                    await DuplicateKeyHandler<TEntity, TKey>.RetryAsUpdateAsync(
                        entity, index, context, operation, cancellationToken);
                }
                return;
            }

            BatchLogger.LogEntityFailed(context.Logger, typeof(TEntity).Name,
                context.GetEntityIdString(entity), FailureClassifier.Classify(ex).ToString());
            operation.RecordFailure(entity, index, ex, context);
        }
        finally
        {
            operation.CleanupEntity(entity, context);
        }
    }

    // === SYNC METHODS ===

    internal BatchResult<TKey> Execute(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchOperation<TEntity, TKey> operation)
    {
        operation.ValidateAll(entities, context);
        context.DetachAllEntities(entities);

        foreach (var entity in entities)
        {
            ProcessSingleEntity(entity, context, operation);
        }

        return operation.CreateResult();
    }

    internal InsertBatchResult<TKey> ExecuteInsert(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchInsertOperation<TEntity, TKey> operation)
    {
        operation.ValidateAll(entities, context);
        context.DetachAllEntities(entities);

        for (var i = 0; i < entities.Count; i++)
        {
            ProcessSingleInsert(entities[i], i, context, operation);
        }

        return operation.CreateResult();
    }

    internal UpsertBatchResult<TKey> ExecuteUpsert(
        List<TEntity> entities,
        BatchStrategyContext<TEntity, TKey> context,
        IBatchUpsertOperation<TEntity, TKey> operation)
    {
        operation.ValidateAll(entities, context);
        context.DetachAllEntities(entities);

        for (var i = 0; i < entities.Count; i++)
        {
            ProcessSingleUpsert(entities[i], i, context, operation);
        }

        return operation.CreateResult();
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
            BatchLogger.LogEntityFailed(context.Logger, typeof(TEntity).Name,
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
            BatchLogger.LogEntityFailed(context.Logger, typeof(TEntity).Name,
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

            BatchLogger.LogEntityFailed(context.Logger, typeof(TEntity).Name,
                context.GetEntityIdString(entity), FailureClassifier.Classify(ex).ToString());
            operation.RecordFailure(entity, index, ex, context);
        }
        finally
        {
            operation.CleanupEntity(entity, context);
        }
    }
}
