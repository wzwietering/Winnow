using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Winnow.Internal;

/// <summary>
/// Wraps SaveChanges with exponential backoff retry for transient failures.
/// </summary>
internal static class SaveChangesRetryHandler
{
    internal static async Task SaveWithRetryAsync(
        DbContext context,
        RetryOptions? retryOptions,
        ILogger? logger,
        Action incrementRetry,
        CancellationToken cancellationToken)
    {
        if (retryOptions is null)
        {
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        // Snapshot values to prevent mutation during async execution
        var maxRetries = retryOptions.MaxRetries;
        var backoffMultiplier = retryOptions.BackoffMultiplier;
        var isTransient = retryOptions.IsTransient;
        var attempt = 0;
        var delay = retryOptions.InitialDelay;

        while (true)
        {
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxRetries && ShouldRetry(ex, isTransient))
            {
                attempt++;
                incrementRetry();
                BatchLogger.LogRetryAttempt(logger, attempt, maxRetries, delay.TotalMilliseconds, ex.Message);
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * backoffMultiplier);
            }
        }
    }

    internal static void SaveWithRetry(
        DbContext context,
        RetryOptions? retryOptions,
        ILogger? logger,
        Action incrementRetry)
    {
        if (retryOptions is null)
        {
            context.SaveChanges();
            return;
        }

        // Snapshot values to prevent mutation during execution
        var maxRetries = retryOptions.MaxRetries;
        var backoffMultiplier = retryOptions.BackoffMultiplier;
        var isTransient = retryOptions.IsTransient;
        var attempt = 0;
        var delay = retryOptions.InitialDelay;

        while (true)
        {
            try
            {
                context.SaveChanges();
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxRetries && ShouldRetry(ex, isTransient))
            {
                attempt++;
                incrementRetry();
                BatchLogger.LogRetryAttempt(logger, attempt, maxRetries, delay.TotalMilliseconds, ex.Message);
                Thread.Sleep(delay);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * backoffMultiplier);
            }
        }
    }

    private static bool ShouldRetry(Exception ex, Func<Exception, bool>? isTransient)
    {
        if (isTransient is not null)
            return isTransient(ex);

        return FailureClassifier.IsTransient(ex);
    }
}
