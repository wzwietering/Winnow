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
    private const string NullEntityMessage = "Entity was null.";
    private const string NullEntityCode = ValidationErrorCodes.NullEntity;

    /// <summary>
    /// Validates <paramref name="entities"/> using <paramref name="validation"/>.
    /// </summary>
    /// <remarks>
    /// Hot-path discipline:
    /// <list type="bullet">
    ///   <item>One delegate cast, captured before the loop.</item>
    ///   <item>One 4-slot inline buffer allocated per batch and reused across every
    ///   entity. The buffer is heap-allocated because <see cref="ValidationError"/>
    ///   contains managed references and cannot live on the stack; the per-batch
    ///   amortisation keeps the cost well below one allocation per entity. The
    ///   collector only rents from <see cref="System.Buffers.ArrayPool{T}"/> when a
    ///   single entity emits more than 4 errors.</item>
    ///   <item><c>for</c> loop indexed against <see cref="List{T}"/> — no enumerator,
    ///   no LINQ.</item>
    ///   <item>Cancellation token is polled every
    ///   <see cref="ValidationOptions.CancellationCheckInterval"/> entities, not per
    ///   iteration.</item>
    ///   <item>The survivor list is sized to the input count, so it never
    ///   reallocates in the all-valid case.</item>
    /// </list>
    /// Navigation walking is enabled when <paramref name="validation"/> is a
    /// <see cref="GraphValidationOptions"/> with
    /// <see cref="GraphValidationOptions.IncludeNavigations"/> set — the type
    /// itself encodes the flat-vs-graph distinction.
    /// </remarks>
    internal static PreValidationResult<TEntity> Run<TEntity>(
        List<TEntity> entities,
        ValidationOptions validation,
        Action<int, string, IReadOnlyList<ValidationError>> recordFailure,
        NavigationFilter? navigationFilter,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        EnsureEntityTypeMatches<TEntity>(validation);
        var ctx = BuildContext<TEntity>(entities.Count, validation, recordFailure, navigationFilter);
        ScanEntities(entities, validation.CancellationCheckInterval, cancellationToken, ref ctx);
        ThrowIfAnyFailed(ctx.ThrownFailures);
        return BuildResult<TEntity>(entities.Count, ctx.Survivors, ctx.Indices, ctx.SurvivorCount);
    }

    private static ValidationRunContext<TEntity> BuildContext<TEntity>(
        int inputCount,
        ValidationOptions validation,
        Action<int, string, IReadOnlyList<ValidationError>> recordFailure,
        NavigationFilter? navigationFilter)
        where TEntity : class =>
        new()
        {
            Validator = (ValidatorDelegate<TEntity>)validation.Validator,
            RecordFailure = recordFailure,
            ThrowOnAny = validation.FailureBehavior == ValidationFailureBehavior.Throw,
            IncludeNavigations = validation.ShouldWalkNavigations,
            NavigationDepthLimit = validation.NavigationDepthLimit,
            NavigationFilter = navigationFilter,
            InlineBuffer = new ValidationError[ValidationCollector.InlineCapacity],
            Survivors = new List<TEntity>(inputCount),
            Indices = new int[inputCount],
        };

    private static void ScanEntities<TEntity>(
        List<TEntity> entities, int interval, CancellationToken cancellationToken,
        ref ValidationRunContext<TEntity> ctx)
        where TEntity : class
    {
        for (int i = 0; i < entities.Count; i++)
        {
            if ((i % interval) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            ProcessEntity(entities[i], i, ref ctx);
        }
    }

    private static void ProcessEntity<TEntity>(
        TEntity? entity, int index, ref ValidationRunContext<TEntity> ctx)
        where TEntity : class
    {
        if (entity is null)
        {
            RecordNullEntity(index, ref ctx);
            return;
        }

        var collector = new ValidationCollector(ctx.InlineBuffer);
        try
        {
            ctx.Validator(entity, ref collector);
            if (ctx.IncludeNavigations)
            {
                NavigationWalker.Walk(entity, ref collector, ctx.NavigationDepthLimit, ctx.NavigationFilter);
            }
            if (collector.IsValid)
            {
                AddSurvivor(ref ctx, entity, index);
                return;
            }
            DispatchFailure(index, collector.AsSpan().ToArray(), ref ctx);
        }
        finally
        {
            collector.Dispose();
        }
    }

    private static void DispatchFailure<TEntity>(
        int index, ValidationError[] errors, ref ValidationRunContext<TEntity> ctx)
        where TEntity : class
    {
        var message = BuildMessage(errors);
        if (ctx.ThrowOnAny)
        {
            (ctx.ThrownFailures ??= []).Add(new WinnowEntityFailure(index, errors, message));
        }
        else
        {
            ctx.RecordFailure(index, message, errors);
        }
    }

    private static void RecordNullEntity<TEntity>(
        int index, ref ValidationRunContext<TEntity> ctx)
        where TEntity : class
    {
        var errors = new[] { new ValidationError(string.Empty, NullEntityMessage, NullEntityCode) };
        if (ctx.ThrowOnAny)
        {
            (ctx.ThrownFailures ??= []).Add(new WinnowEntityFailure(index, errors, NullEntityMessage));
        }
        else
        {
            ctx.RecordFailure(index, NullEntityMessage, errors);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddSurvivor<TEntity>(
        ref ValidationRunContext<TEntity> ctx, TEntity entity, int originalIndex)
        where TEntity : class
    {
        ctx.Survivors.Add(entity);
        ctx.Indices[ctx.SurvivorCount] = originalIndex;
        ctx.SurvivorCount++;
    }

    private static void ThrowIfAnyFailed(List<WinnowEntityFailure>? thrownFailures)
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
        int[] trimmed = survivorCount switch
        {
            0 => Array.Empty<int>(),
            var n when n == inputCount => indices,
            _ => indices.AsSpan(0, survivorCount).ToArray(),
        };
        return new PreValidationResult<TEntity>(survivors, trimmed);
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

/// <summary>
/// Per-batch state for <see cref="PreValidationRunner.Run{TEntity}"/>. Holds
/// the loop-invariant validator configuration plus the mutable accumulators
/// (survivor list, original-index map, thrown-failures buffer). Passed by
/// <c>ref</c> so the scan-and-record helpers can mutate <see cref="SurvivorCount"/>
/// and <see cref="ThrownFailures"/> without taking a dozen ref parameters each.
/// </summary>
internal struct ValidationRunContext<TEntity>
    where TEntity : class
{
    public ValidatorDelegate<TEntity> Validator;
    public Action<int, string, IReadOnlyList<ValidationError>> RecordFailure;
    public bool ThrowOnAny;
    public bool IncludeNavigations;
    public int NavigationDepthLimit;
    public NavigationFilter? NavigationFilter;
    public ValidationError[] InlineBuffer;
    public List<TEntity> Survivors;
    public int[] Indices;
    public List<WinnowEntityFailure>? ThrownFailures;
    public int SurvivorCount;
}
