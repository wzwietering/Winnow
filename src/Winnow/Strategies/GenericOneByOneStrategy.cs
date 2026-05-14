using Winnow.Internal;
using Microsoft.Extensions.Logging;

namespace Winnow.Strategies;

/// <summary>
/// Generic one-by-one batch processing strategy.
/// Processes each entity individually with separate SaveChanges calls.
/// Maximum failure isolation but more database round trips.
/// </summary>
internal class GenericOneByOneStrategy<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    internal async Task<WinnowResult<TKey>> ExecuteAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        IOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        var survivors = operation.ApplyPreValidation(entities, context, cancellationToken);
        operation.ValidateAll(survivors, context);
        context.DetachAllEntities(survivors);

        var wasCancelled = false;
        foreach (var entity in survivors)
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

    internal async Task<InsertResult<TKey>> ExecuteInsertAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        IInsertOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        var preValidated = operation.ApplyPreValidation(entities, context, cancellationToken);
        operation.ValidateAll(preValidated.Survivors, context);
        context.DetachAllEntities(preValidated.Survivors);

        var wasCancelled = false;
        for (var i = 0; i < preValidated.Survivors.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                wasCancelled = true;
                break;
            }

            await ProcessSingleInsertAsync(
                preValidated.Survivors[i], preValidated.GetOriginalIndex(i), context, operation, cancellationToken);
        }

        return operation.CreateResult(wasCancelled);
    }

    internal async Task<UpsertResult<TKey>> ExecuteUpsertAsync(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        IUpsertOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        var preValidated = operation.ApplyPreValidation(entities, context, cancellationToken);
        operation.ValidateAll(preValidated.Survivors, context);
        // MatchBy pre-SELECT fires once per batch here, before per-entity processing.
        // New upsert strategies must mirror this call at their batch entry, otherwise
        // MatchBy silently skips and routing falls back to PK default-value detection.
        if (operation is IMatchByCapableOperation<TEntity, TKey> matchByOp)
        {
            await matchByOp.ResolveBatchAsync(
                preValidated.Survivors, preValidated.OriginalIndices, entities.Count, context, cancellationToken);
        }
        context.DetachAllEntities(preValidated.Survivors);

        var wasCancelled = false;
        for (var i = 0; i < preValidated.Survivors.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                wasCancelled = true;
                break;
            }

            await ProcessSingleUpsertAsync(
                preValidated.Survivors[i], preValidated.GetOriginalIndex(i), context, operation, cancellationToken);
        }

        return operation.CreateResult(wasCancelled);
    }

    private static async Task ProcessSingleEntityAsync(
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

    private static async Task ProcessSingleInsertAsync(
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

    private static async Task ProcessSingleUpsertAsync(
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
                    var wasCancelled = await DuplicateKeyHandler<TEntity, TKey>.RetryAsUpdateAsync(
                        entity, index, context, operation, cancellationToken);
                    if (wasCancelled)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }
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

    internal WinnowResult<TKey> Execute(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        IOperation<TEntity, TKey> operation)
    {
        var survivors = operation.ApplyPreValidation(entities, context, CancellationToken.None);
        operation.ValidateAll(survivors, context);
        context.DetachAllEntities(survivors);

        foreach (var entity in survivors)
        {
            ProcessSingleEntity(entity, context, operation);
        }

        return operation.CreateResult();
    }

    internal InsertResult<TKey> ExecuteInsert(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        IInsertOperation<TEntity, TKey> operation)
    {
        var preValidated = operation.ApplyPreValidation(entities, context, CancellationToken.None);
        operation.ValidateAll(preValidated.Survivors, context);
        context.DetachAllEntities(preValidated.Survivors);

        for (var i = 0; i < preValidated.Survivors.Count; i++)
        {
            ProcessSingleInsert(preValidated.Survivors[i], preValidated.GetOriginalIndex(i), context, operation);
        }

        return operation.CreateResult();
    }

    internal UpsertResult<TKey> ExecuteUpsert(
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        IUpsertOperation<TEntity, TKey> operation)
    {
        var preValidated = operation.ApplyPreValidation(entities, context, CancellationToken.None);
        operation.ValidateAll(preValidated.Survivors, context);
        // MatchBy pre-SELECT fires once per batch here, before per-entity processing.
        // New upsert strategies must mirror this call at their batch entry.
        if (operation is IMatchByCapableOperation<TEntity, TKey> matchByOp)
        {
            matchByOp.ResolveBatch(
                preValidated.Survivors, preValidated.OriginalIndices, entities.Count, context);
        }
        context.DetachAllEntities(preValidated.Survivors);

        for (var i = 0; i < preValidated.Survivors.Count; i++)
        {
            ProcessSingleUpsert(preValidated.Survivors[i], preValidated.GetOriginalIndex(i), context, operation);
        }

        return operation.CreateResult();
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
}
