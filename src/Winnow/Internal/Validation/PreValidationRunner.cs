using System.Runtime.CompilerServices;

namespace Winnow.Internal.Validation;

/// <summary>
/// Runs the configured <see cref="ValidationOptions"/> across an entity batch
/// before any database round trip. Records failures via a caller-supplied
/// callback (so the same engine drives both index-keyed and id-keyed accumulators)
/// and returns the survivors with their original input indices preserved.
/// </summary>
internal static class PreValidationRunner
{
    /// <summary>
    /// Validates <paramref name="entities"/> using <paramref name="validation"/>.
    /// </summary>
    /// <remarks>
    /// Hot-path discipline:
    /// <list type="bullet">
    ///   <item>One delegate cast, captured before the loop.</item>
    ///   <item>Per-entity validator gets a stack-allocated 4-slot
    ///   <see cref="ValidationCollector"/>. The collector only rents from the array
    ///   pool when a single entity emits more than 4 errors.</item>
    ///   <item><c>for</c> loop indexed against <see cref="List{T}"/> — no enumerator,
    ///   no LINQ.</item>
    ///   <item>Cancellation token is polled every
    ///   <see cref="ValidationOptions.CancellationCheckInterval"/> entities, not per
    ///   iteration.</item>
    ///   <item>The survivor list is sized to the input count, so it never
    ///   reallocates in the all-valid case.</item>
    /// </list>
    /// </remarks>
    internal static PreValidationResult<TEntity> Run<TEntity>(
        List<TEntity> entities,
        ValidationOptions validation,
        Action<int, string, IReadOnlyList<ValidationError>> recordFailure,
        bool isGraphOperation,
        NavigationFilter? navigationFilter,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        EnsureEntityTypeMatches<TEntity>(validation);
        // IncludeNavigations is documented as a no-op on flat operations; the
        // typed-delegate compatibility check then also only fires for the graph
        // path where the walker can actually be invoked.
        var effectiveIncludeNavigations = validation.IncludeNavigations && isGraphOperation;
        EnsureIncludeNavigationsCompatible(validation, effectiveIncludeNavigations);
        var validator = (ValidatorDelegate<TEntity>)validation.Validator;
        var throwOnAny = validation.FailureBehavior == ValidationFailureBehavior.Throw;
        var survivors = new List<TEntity>(entities.Count);
        var indices = new int[entities.Count];
        // Only allocate the throw-mode collector lazily; RecordAsFailure mode never writes here.
        List<EntityValidationFailure>? thrownFailures = null;
        var inlineBuffer = new ValidationError[ValidationCollector.InlineCapacity];
        int survivorCount = 0;

        ScanEntities(entities, validator, recordFailure, validation.CancellationCheckInterval,
            cancellationToken, throwOnAny, effectiveIncludeNavigations, navigationFilter,
            inlineBuffer, survivors, indices, ref thrownFailures, ref survivorCount);

        ThrowIfAnyFailed(thrownFailures);
        return BuildResult(entities.Count, survivors, indices, survivorCount);
    }

    private static void ScanEntities<TEntity>(
        List<TEntity> entities,
        ValidatorDelegate<TEntity> validator,
        Action<int, string, IReadOnlyList<ValidationError>> recordFailure,
        int interval,
        CancellationToken cancellationToken,
        bool throwOnAny,
        bool includeNavigations,
        NavigationFilter? navigationFilter,
        ValidationError[] inlineBuffer,
        List<TEntity> survivors,
        int[] indices,
        ref List<EntityValidationFailure>? thrownFailures,
        ref int survivorCount)
        where TEntity : class
    {
        for (int i = 0; i < entities.Count; i++)
        {
            if ((i % interval) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            ProcessEntity(entities[i], i, validator, recordFailure, throwOnAny, includeNavigations, navigationFilter,
                inlineBuffer, survivors, indices, ref thrownFailures, ref survivorCount);
        }
    }

    private static void ProcessEntity<TEntity>(
        TEntity? entity,
        int index,
        ValidatorDelegate<TEntity> validator,
        Action<int, string, IReadOnlyList<ValidationError>> recordFailure,
        bool throwOnAny,
        bool includeNavigations,
        NavigationFilter? navigationFilter,
        ValidationError[] inlineBuffer,
        List<TEntity> survivors,
        int[] indices,
        ref List<EntityValidationFailure>? thrownFailures,
        ref int survivorCount)
        where TEntity : class
    {
        if (entity is null)
        {
            RecordNullEntity(index, recordFailure, ref thrownFailures, throwOnAny);
            return;
        }

        var collector = new ValidationCollector(inlineBuffer);
        try
        {
            validator(entity, ref collector);
            if (includeNavigations)
            {
                NavigationWalker.Walk(entity, ref collector, navigationFilter);
            }
            if (collector.IsValid)
            {
                AddSurvivor(survivors, indices, ref survivorCount, entity, index);
                return;
            }
            DispatchFailure(index, collector.AsSpan().ToArray(), recordFailure, throwOnAny, ref thrownFailures);
        }
        finally
        {
            collector.Dispose();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EnsureIncludeNavigationsCompatible(ValidationOptions validation, bool effectiveIncludeNavigations)
    {
        if (!effectiveIncludeNavigations) return;
        if (validation.IsDataAnnotationsValidator) return;
        throw new InvalidOperationException(
            "IncludeNavigations only supports validators built by WithDataAnnotations. " +
            "Typed ValidatorDelegate<TEntity> cannot be applied polymorphically to children " +
            "of differing types — wire WithDataAnnotations<TEntity>() instead, or set " +
            "IncludeNavigations = false and validate children with a separate options instance.");
    }

    private static void DispatchFailure(
        int index,
        ValidationError[] errors,
        Action<int, string, IReadOnlyList<ValidationError>> recordFailure,
        bool throwOnAny,
        ref List<EntityValidationFailure>? thrownFailures)
    {
        var message = BuildMessage(errors);
        if (throwOnAny)
        {
            (thrownFailures ??= []).Add(new EntityValidationFailure(index, message, errors));
        }
        else
        {
            recordFailure(index, message, errors);
        }
    }

    private static void ThrowIfAnyFailed(List<EntityValidationFailure>? thrownFailures)
    {
        if (thrownFailures is { Count: > 0 })
        {
            throw new WinnowValidationException(thrownFailures);
        }
    }

    private static PreValidationResult<TEntity> BuildResult<TEntity>(
        int inputCount, List<TEntity> survivors, int[] indices, int survivorCount)
        where TEntity : class
    {
        // Trim indices array to actual survivor count so callers don't see
        // padding from skipped invalid entries.
        int[] trimmed = survivorCount switch
        {
            0 => Array.Empty<int>(),
            var n when n == inputCount => indices,
            _ => indices.AsSpan(0, survivorCount).ToArray(),
        };
        return new PreValidationResult<TEntity>(survivors, trimmed);
    }

    private const string NullEntityMessage = "Entity was null.";
    private const string NullEntityCode = "WINNOW_NULL_ENTITY";

    private static void RecordNullEntity(
        int index,
        Action<int, string, IReadOnlyList<ValidationError>> recordFailure,
        ref List<EntityValidationFailure>? thrownFailures,
        bool throwOnAny)
    {
        var errors = new[] { new ValidationError(string.Empty, NullEntityMessage, NullEntityCode) };
        if (throwOnAny)
        {
            (thrownFailures ??= []).Add(new EntityValidationFailure(index, NullEntityMessage, errors));
        }
        else
        {
            recordFailure(index, NullEntityMessage, errors);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddSurvivor<TEntity>(
        List<TEntity> survivors,
        int[] indices,
        ref int survivorCount,
        TEntity entity,
        int originalIndex)
    {
        survivors.Add(entity);
        indices[survivorCount] = originalIndex;
        survivorCount++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EnsureEntityTypeMatches<TEntity>(ValidationOptions validation)
    {
        if (validation.EntityType == typeof(TEntity))
        {
            return;
        }
        throw new InvalidOperationException(
            $"ValidationOptions was configured for entity type '{validation.EntityType.Name}', " +
            $"but the current operation targets '{typeof(TEntity).Name}'. Use the WithValidation overload " +
            "matching the operation's entity type.");
    }

    /// <summary>
    /// Formats one entity's collected errors into the single string stored on the
    /// failure record. Fast-paths the one-error case (the common shape); for the
    /// multi-error case uses <see cref="string.Join(string, IEnumerable{string})"/>
    /// over a small array, which is more than adequate at the 2–4 error scale
    /// the collector inlines.
    /// </summary>
    private static string BuildMessage(ValidationError[] errors)
    {
        if (errors.Length == 1)
        {
            return FormatOne(errors[0]);
        }

        var parts = new string[errors.Length];
        for (int i = 0; i < errors.Length; i++)
        {
            parts[i] = FormatOne(errors[i]);
        }
        return string.Join("; ", parts);
    }

    private static string FormatOne(in ValidationError error) =>
        string.IsNullOrEmpty(error.PropertyName)
            ? error.Message
            : $"{error.PropertyName}: {error.Message}";
}
