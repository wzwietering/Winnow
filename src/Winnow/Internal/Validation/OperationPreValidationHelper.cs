using Microsoft.Extensions.Logging;
using Winnow.Internal.Accumulators;

namespace Winnow.Internal.Validation;

/// <summary>
/// Shared shims that operations call from their <c>ApplyPreValidation</c>
/// implementations. Centralises the "no validation configured" short-circuit,
/// the accumulator failure recording, and the
/// <see cref="UpsertOperationType"/> heuristic for upsert pre-validation
/// failures.
/// </summary>
internal static class OperationPreValidationHelper
{
    internal static List<TEntity> Run<TEntity, TKey>(
        ValidationOptions? validation,
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        WinnowAccumulator<TKey> accumulator,
        CancellationToken cancellationToken)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        if (validation is null)
        {
            return entities;
        }
        var result = RunCore(validation, entities, context.Logger, cancellationToken,
            (originalIndex, message, _) => accumulator.RecordFailure(
                ReadIdOrDefault(context, entities[originalIndex]),
                message,
                FailureReason.ValidationError,
                exception: null));
        return result.Survivors;
    }

    internal static PreValidationResult<TEntity> RunIndexed<TEntity, TKey>(
        ValidationOptions? validation,
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        InsertAccumulator<TKey> accumulator,
        CancellationToken cancellationToken)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        if (validation is null)
        {
            return PreValidationResult<TEntity>.Passthrough(entities);
        }
        return RunCore(validation, entities, context.Logger, cancellationToken,
            (originalIndex, message, _) => accumulator.RecordFailure(
                originalIndex,
                message,
                FailureReason.ValidationError,
                exception: null));
    }

    internal static PreValidationResult<TEntity> RunIndexed<TEntity, TKey>(
        ValidationOptions? validation,
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        UpsertAccumulator<TKey> accumulator,
        CancellationToken cancellationToken)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        if (validation is null)
        {
            return PreValidationResult<TEntity>.Passthrough(entities);
        }
        return RunCore(validation, entities, context.Logger, cancellationToken,
            (originalIndex, message, _) => RecordUpsertFailure(
                originalIndex, message, entities, context, accumulator));
    }

    private static void RecordUpsertFailure<TEntity, TKey>(
        int originalIndex,
        string message,
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        UpsertAccumulator<TKey> accumulator)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        var entity = entities[originalIndex];
        var isInsert = entity is null || context.HasDefaultKeyValue(entity);
        accumulator.RecordFailure(
            originalIndex,
            isInsert ? default : ReadIdOrDefault(context, entity),
            message,
            FailureReason.ValidationError,
            exception: null,
            isInsert ? UpsertOperationType.Insert : UpsertOperationType.Update);
    }

    private static PreValidationResult<TEntity> RunCore<TEntity>(
        ValidationOptions validation,
        List<TEntity> entities,
        ILogger? logger,
        CancellationToken cancellationToken,
        Action<int, string, IReadOnlyList<ValidationError>> recordFailure)
        where TEntity : class
    {
        var result = PreValidationRunner.Run<TEntity>(entities, validation, recordFailure, cancellationToken);
        LogIfFiltered(logger, typeof(TEntity), entities.Count, result.Survivors.Count);
        return result;
    }

    private static void LogIfFiltered(ILogger? logger, Type entityType, int inputCount, int survivorCount)
    {
        if (inputCount == survivorCount) return;
        WinnowLogger.LogPreValidationFiltered(logger, entityType.Name, inputCount, survivorCount);
    }

    private static TKey ReadIdOrDefault<TEntity, TKey>(
        StrategyContext<TEntity, TKey> context, TEntity? entity)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        if (entity is null)
        {
            return default!;
        }
        return SuppressKeyReadFailures(() => context.GetEntityIdFromInstance(entity));
    }

    /// <summary>
    /// Reads a key value, suppressing only expected pre-validation failures
    /// (e.g. shadow PKs, model misconfiguration). Fatal exceptions (<see cref="OutOfMemoryException"/>,
    /// <see cref="StackOverflowException"/>) and cooperative cancellation
    /// (<see cref="OperationCanceledException"/>) propagate so the caller can tear down
    /// the operation properly. Pre-validation runs before any tracker work, so
    /// expected failures fall back to <c>default(TKey)</c> and rely on the failure
    /// message to identify the entity.
    /// </summary>
    internal static TKey SuppressKeyReadFailures<TKey>(Func<TKey> reader)
        where TKey : notnull, IEquatable<TKey>
    {
        try
        {
            return reader();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException
                                   and not StackOverflowException
                                   and not OperationCanceledException)
        {
            return default!;
        }
    }
}
