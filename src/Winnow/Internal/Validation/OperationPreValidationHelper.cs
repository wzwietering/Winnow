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
        NavigationFilter? navigationFilter,
        CancellationToken cancellationToken)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        if (validation is null)
        {
            return entities;
        }
        var result = RunCore(validation, entities, context.Logger, navigationFilter, cancellationToken,
            (originalIndex, message, errors) => accumulator.RecordFailure(
                ReadIdOrDefault(context, entities[originalIndex]),
                message,
                FailureReason.PreValidationError,
                exception: null,
                validationErrors: errors));
        return result.Survivors;
    }

    internal static PreValidationResult<TEntity> RunIndexed<TEntity, TKey>(
        ValidationOptions? validation,
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        InsertAccumulator<TKey> accumulator,
        NavigationFilter? navigationFilter,
        CancellationToken cancellationToken)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        if (validation is null)
        {
            return PreValidationResult<TEntity>.Passthrough(entities);
        }
        return RunCore(validation, entities, context.Logger, navigationFilter, cancellationToken,
            (originalIndex, message, errors) => accumulator.RecordFailure(
                originalIndex,
                message,
                FailureReason.PreValidationError,
                exception: null,
                validationErrors: errors));
    }

    internal static PreValidationResult<TEntity> RunIndexed<TEntity, TKey>(
        ValidationOptions? validation,
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        UpsertAccumulator<TKey> accumulator,
        NavigationFilter? navigationFilter,
        CancellationToken cancellationToken)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        if (validation is null)
        {
            return PreValidationResult<TEntity>.Passthrough(entities);
        }
        return RunCore(validation, entities, context.Logger, navigationFilter, cancellationToken,
            (originalIndex, message, errors) => RecordUpsertFailure(
                originalIndex, message, errors, entities, context, accumulator));
    }

    private static void RecordUpsertFailure<TEntity, TKey>(
        int originalIndex,
        string message,
        IReadOnlyList<ValidationError> errors,
        List<TEntity> entities,
        StrategyContext<TEntity, TKey> context,
        UpsertAccumulator<TKey> accumulator)
        where TEntity : class
        where TKey : notnull, IEquatable<TKey>
    {
        // Null entries reach this lambda via PreValidationRunner.RecordNullEntity, which
        // invokes the same recordFailure callback. Treat them as inserts so we don't
        // dereference null when probing the key state.
        var entity = entities[originalIndex];
        var isInsert = entity is null || context.HasDefaultKeyValue(entity);
        accumulator.RecordFailure(
            originalIndex,
            isInsert ? default : ReadIdOrDefault(context, entity),
            message,
            FailureReason.PreValidationError,
            exception: null,
            isInsert ? UpsertOperationType.Insert : UpsertOperationType.Update,
            validationErrors: errors);
    }

    private static PreValidationResult<TEntity> RunCore<TEntity>(
        ValidationOptions validation,
        List<TEntity> entities,
        ILogger? logger,
        NavigationFilter? navigationFilter,
        CancellationToken cancellationToken,
        Action<int, string, IReadOnlyList<ValidationError>> recordFailure)
        where TEntity : class
    {
        var result = PreValidationRunner.Run<TEntity>(entities, validation, recordFailure, navigationFilter, cancellationToken);
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
    /// Reads a key value, suppressing only the <see cref="InvalidOperationException"/>
    /// EF Core itself raises for shadow primary keys or model misconfiguration during
    /// the pre-validation phase (before tracker work). User-thrown
    /// <see cref="InvalidOperationException"/>s — and all other exception types,
    /// including programmer errors such as <see cref="NullReferenceException"/> —
    /// propagate so they surface as the real bug rather than collapsing to
    /// <c>default(TKey)</c>.
    /// </summary>
    internal static TKey SuppressKeyReadFailures<TKey>(Func<TKey> reader)
        where TKey : notnull, IEquatable<TKey>
    {
        try
        {
            return reader();
        }
        catch (InvalidOperationException ex) when (EfExceptionFilter.IsEntityFrameworkInvalidOperation(ex))
        {
            return default!;
        }
    }
}
