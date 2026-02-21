using Microsoft.Extensions.Logging;

namespace EfCoreUtils.Internal;

/// <summary>
/// Zero-allocation structured logging for batch operations.
/// All methods are no-ops when logger is null.
/// </summary>
internal static partial class BatchLogger
{
    internal static void LogBatchStarting(
        ILogger? logger, string operation, string entityType, int count, BatchStrategy strategy)
    {
        if (logger is null) return;
        BatchStarting(logger, operation, entityType, count, strategy);
    }

    internal static void LogBatchCompleted(
        ILogger? logger, string operation, string entityType,
        int successCount, int failureCount, double durationMs, int roundTrips)
    {
        if (logger is null) return;
        BatchCompleted(logger, operation, entityType, successCount, failureCount, durationMs, roundTrips);
    }

    internal static void LogEntityFailed(
        ILogger? logger, string entityType, string entityId, string reason)
    {
        if (logger is null) return;
        EntityFailed(logger, entityType, entityId, reason);
    }

    internal static void LogDivideAndConquerSplit(
        ILogger? logger, int originalCount, int leftCount, int rightCount)
    {
        if (logger is null) return;
        DivideAndConquerSplit(logger, originalCount, leftCount, rightCount);
    }

    internal static void LogRetryAttempt(
        ILogger? logger, int attempt, int maxRetries, double delayMs, string error)
    {
        if (logger is null) return;
        RetryAttempt(logger, attempt, maxRetries, delayMs, error);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "{Operation} starting for {EntityType}: {Count} entities using {Strategy}")]
    private static partial void BatchStarting(
        ILogger logger, string operation, string entityType, int count, BatchStrategy strategy);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "{Operation} completed for {EntityType}: {SuccessCount} succeeded, {FailureCount} failed in {DurationMs:F1}ms ({RoundTrips} round trips)")]
    private static partial void BatchCompleted(
        ILogger logger, string operation, string entityType,
        int successCount, int failureCount, double durationMs, int roundTrips);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Entity {EntityType} [{EntityId}] failed: {Reason}")]
    private static partial void EntityFailed(
        ILogger logger, string entityType, string entityId, string reason);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Divide and conquer: splitting {OriginalCount} entities into {LeftCount} + {RightCount}")]
    private static partial void DivideAndConquerSplit(
        ILogger logger, int originalCount, int leftCount, int rightCount);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Retry attempt {Attempt}/{MaxRetries} after {DelayMs:F0}ms delay: {Error}")]
    private static partial void RetryAttempt(
        ILogger logger, int attempt, int maxRetries, double delayMs, string error);
}
