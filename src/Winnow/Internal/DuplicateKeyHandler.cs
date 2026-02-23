using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Winnow.Internal;

/// <summary>
/// Handles duplicate key detection and retry logic for upsert operations.
/// </summary>
internal static class DuplicateKeyHandler<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    /// <summary>
    /// Determines if a duplicate key error should be handled based on the operation's strategy.
    /// </summary>
    internal static bool ShouldHandle(
        Exception ex,
        int index,
        IUpsertOperation<TEntity, TKey> operation,
        out DuplicateKeyStrategy strategy)
    {
        strategy = operation.DuplicateKeyStrategy;

        if (strategy == DuplicateKeyStrategy.Fail)
            return false;

        if (!operation.WasInsertAttempt(index))
            return false;

        return FailureClassifier.Classify(ex) == FailureReason.DuplicateKey;
    }

    /// <summary>
    /// Retries a failed insert as an update operation (sync version).
    /// </summary>
    internal static void RetryAsUpdate(
        TEntity entity,
        int index,
        StrategyContext<TEntity, TKey> context,
        IUpsertOperation<TEntity, TKey> operation)
    {
        try
        {
            context.Context.Entry(entity).State = EntityState.Modified;
            SaveChangesRetryHandler.SaveWithRetry(context.Context, context.RetryOptions, context.Logger, context.IncrementRetryCount);
            context.IncrementRoundTrip();
            operation.RecordSuccessAsUpdate(entity, index, context);
        }
        catch (Exception retryEx)
        {
            context.IncrementRoundTrip();
            operation.RecordFailure(entity, index, retryEx, context);
        }
    }

    /// <summary>
    /// Retries a failed insert as an update operation (async version).
    /// Returns true if the operation was cancelled.
    /// </summary>
    internal static async Task<bool> RetryAsUpdateAsync(
        TEntity entity,
        int index,
        StrategyContext<TEntity, TKey> context,
        IUpsertOperation<TEntity, TKey> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            context.Context.Entry(entity).State = EntityState.Modified;
            await SaveChangesRetryHandler.SaveWithRetryAsync(context.Context, context.RetryOptions, context.Logger, context.IncrementRetryCount, cancellationToken);
            context.IncrementRoundTrip();
            operation.RecordSuccessAsUpdate(entity, index, context);
            return false;
        }
        catch (OperationCanceledException)
        {
            context.IncrementRoundTrip();
            return true;
        }
        catch (Exception retryEx)
        {
            context.IncrementRoundTrip();
            operation.RecordFailure(entity, index, retryEx, context);
            return false;
        }
    }
}
