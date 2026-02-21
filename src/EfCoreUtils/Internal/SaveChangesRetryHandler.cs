using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EfCoreUtils.Internal;

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
            catch (Exception ex) when (attempt < retryOptions.MaxRetries && ShouldRetry(ex, retryOptions))
            {
                attempt++;
                incrementRetry();
                BatchLogger.LogRetryAttempt(logger, attempt, retryOptions.MaxRetries, delay.TotalMilliseconds, ex.Message);
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * retryOptions.BackoffMultiplier);
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

        var attempt = 0;
        var delay = retryOptions.InitialDelay;

        while (true)
        {
            try
            {
                context.SaveChanges();
                return;
            }
            catch (Exception ex) when (attempt < retryOptions.MaxRetries && ShouldRetry(ex, retryOptions))
            {
                attempt++;
                incrementRetry();
                BatchLogger.LogRetryAttempt(logger, attempt, retryOptions.MaxRetries, delay.TotalMilliseconds, ex.Message);
                Thread.Sleep(delay);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * retryOptions.BackoffMultiplier);
            }
        }
    }

    private static bool ShouldRetry(Exception ex, RetryOptions options) =>
        options.IsTransient?.Invoke(ex) ?? FailureClassifier.IsTransient(ex);
}
