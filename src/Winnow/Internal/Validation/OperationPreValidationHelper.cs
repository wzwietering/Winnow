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
        var result = PreValidationRunner.Run<TEntity>(
            entities,
            validation,
            (originalIndex, message, _) =>
                accumulator.RecordFailure(
                    ReadIdOrDefault(context, entities[originalIndex]),
                    message,
                    FailureReason.ValidationError,
                    exception: null),
            cancellationToken);
        LogIfFiltered(context, entities.Count, result.Survivors.Count);
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
        var result = PreValidationRunner.Run<TEntity>(
            entities,
            validation,
            (originalIndex, message, _) =>
                accumulator.RecordFailure(
                    originalIndex,
                    message,
                    FailureReason.ValidationError,
                    exception: null),
            cancellationToken);
        LogIfFiltered(context, entities.Count, result.Survivors.Count);
        return result;
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
        var result = PreValidationRunner.Run<TEntity>(
            entities,
            validation,
            (originalIndex, message, _) =>
            {
                var entity = entities[originalIndex];
                var isInsert = context.HasDefaultKeyValue(entity);
                accumulator.RecordFailure(
                    originalIndex,
                    isInsert ? default : ReadIdOrDefault(context, entity),
                    message,
                    FailureReason.ValidationError,
                    exception: null,
                    isInsert ? UpsertOperationType.Insert : UpsertOperationType.Update);
            },
            cancellationToken);
        LogIfFiltered(context, entities.Count, result.Survivors.Count);
        return result;
    }

    private static void LogIfFiltered<TEntity, TKey>(
        StrategyContext<TEntity, TKey> context, int inputCount, int survivorCount)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        if (inputCount == survivorCount) return;
        WinnowLogger.LogPreValidationFiltered(
            context.Logger, typeof(TEntity).Name, inputCount, survivorCount);
    }

    private static TKey ReadIdOrDefault<TEntity, TKey>(
        StrategyContext<TEntity, TKey> context, TEntity entity)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        try
        {
            return context.GetEntityIdFromInstance(entity);
        }
        catch
        {
            // Pre-validation runs before any tracker work, so a missing/shadow PK
            // shouldn't poison the failure record. Fall back to default(TKey)
            // and rely on the message to identify the entity.
            return default!;
        }
    }
}
