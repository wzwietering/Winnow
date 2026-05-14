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
        CancellationToken cancellationToken)
        where TEntity : class
    {
        EnsureEntityTypeMatches<TEntity>(validation);
        var validator = (ValidatorDelegate<TEntity>)validation.Validator;
        var interval = validation.CancellationCheckInterval;
        var throwOnAny = validation.FailureBehavior == ValidationFailureBehavior.Throw;
        var survivors = new List<TEntity>(entities.Count);
        var indices = new int[entities.Count];
        int survivorCount = 0;
        List<EntityValidationFailure>? thrownFailures = throwOnAny ? [] : null;

        // ValidationError holds string references and so cannot be stackalloc'd.
        // Allocate the inline buffer once per batch on the heap (~192 bytes) and
        // reuse it across every entity; the collector resets its count per entity,
        // and the pool only kicks in when a single entity emits >4 errors.
        var inlineBuffer = new ValidationError[ValidationCollector.InlineCapacity];

        for (int i = 0; i < entities.Count; i++)
        {
            if ((i % interval) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            var entity = entities[i];
            if (entity is null)
            {
                AddSurvivor(survivors, indices, ref survivorCount, entity!, i);
                continue;
            }

            var collector = new ValidationCollector(inlineBuffer);
            try
            {
                validator(entity, ref collector);
                if (collector.IsValid)
                {
                    AddSurvivor(survivors, indices, ref survivorCount, entity, i);
                    continue;
                }

                var errors = collector.AsSpan().ToArray();
                var message = BuildMessage(errors);
                if (throwOnAny)
                {
                    thrownFailures!.Add(new EntityValidationFailure(i, message, errors));
                }
                else
                {
                    recordFailure(i, message, errors);
                }
            }
            finally
            {
                collector.Dispose();
            }
        }

        if (throwOnAny && thrownFailures!.Count > 0)
        {
            throw new ValidationException(thrownFailures);
        }

        // Trim indices array to actual survivor count so callers don't see
        // padding from skipped invalid entries.
        var trimmedIndices = survivorCount == entities.Count ? indices : indices.AsSpan(0, survivorCount).ToArray();
        return new PreValidationResult<TEntity>(survivors, trimmedIndices);
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
