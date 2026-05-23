namespace Winnow;

/// <summary>
/// Configuration for the optional pre-validation pipeline. When attached to
/// <see cref="WinnowOptions.Validation"/> or <see cref="GraphOptionsBase.Validation"/>,
/// entities are validated in-process before any database round trip — invalid
/// entities are recorded as failures with <see cref="FailureReason.PreValidationError"/>
/// and never sent to the strategy.
/// </summary>
/// <remarks>
/// Construct via the <c>WithValidation</c> or <c>WithDataAnnotations</c> extension
/// methods rather than directly — those carry the type information needed to wire
/// the validator to a specific <c>TEntity</c>. The constructor is <c>internal</c>,
/// so external code cannot subclass this type; the only subclass is the
/// library-supplied <see cref="GraphValidationOptions"/>. Sealing it in a future
/// release is therefore non-breaking — there is no external inheritance to break.
/// </remarks>
public class ValidationOptions
{
    private int _cancellationCheckInterval = DefaultCancellationCheckInterval;
    private bool _frozen;

    /// <summary>
    /// Default cancellation poll interval — every 256 entities. Internal so the
    /// value is not part of the public API surface; the JIT can fold it as a
    /// compile-time constant inside the library.
    /// </summary>
    internal const int DefaultCancellationCheckInterval = 256;

    /// <summary>
    /// The entity type the configured validator applies to. Set by the
    /// <c>WithValidation</c> / <c>WithDataAnnotations</c> extension methods so the
    /// pipeline can fail fast if the carrier is attached to options targeting a
    /// different entity type.
    /// </summary>
    internal Type EntityType { get; }

    /// <summary>
    /// Type-erased validator delegate. The concrete type is
    /// <see cref="WinnowValidator{TEntity}"/> for <see cref="EntityType"/>;
    /// the pipeline casts it once per batch and caches the typed reference.
    /// </summary>
    internal object Validator { get; }

    /// <summary>
    /// True when the validator was built by the DataAnnotations adapter
    /// (<c>WithDataAnnotations</c>). Only DataAnnotations validators can be applied
    /// polymorphically to child entities, so this gates navigation walking on
    /// <see cref="GraphValidationOptions"/>.
    /// </summary>
    internal bool IsDataAnnotationsValidator { get; }

    /// <summary>
    /// Controls what happens when at least one entity fails validation.
    /// Default: <see cref="ValidationFailureBehavior.RecordAsFailure"/>.
    /// Must be set before the options object reaches a Winnow operation;
    /// mutating it after the pipeline has read the value throws
    /// <see cref="InvalidOperationException"/> to surface the race rather than
    /// running with mid-batch divergence under <c>ParallelWinnower</c>.
    /// </summary>
    public ValidationFailureBehavior FailureBehavior
    {
        get => _failureBehavior;
        set
        {
            ThrowIfFrozen();
            _failureBehavior = value;
        }
    }
    private ValidationFailureBehavior _failureBehavior = ValidationFailureBehavior.RecordAsFailure;

    /// <summary>
    /// How often the validation pipeline checks the cancellation token, measured
    /// in entities. Default: <see cref="DefaultCancellationCheckInterval"/>.
    /// Must be positive. Lower values give faster cancellation response at a small
    /// throughput cost; higher values trade latency for throughput.
    /// Same freeze semantics as <see cref="FailureBehavior"/>.
    /// </summary>
    public int CancellationCheckInterval
    {
        get => _cancellationCheckInterval;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            ThrowIfFrozen();
            _cancellationCheckInterval = value;
        }
    }

    internal ValidationOptions(Type entityType, object validator, bool isDataAnnotationsValidator)
    {
        EntityType = entityType;
        Validator = validator;
        IsDataAnnotationsValidator = isDataAnnotationsValidator;
    }

    internal static ValidationOptions CreateFlat(Type entityType, object validator, bool isDataAnnotationsValidator = false) =>
        new(entityType, validator, isDataAnnotationsValidator);

    /// <summary>
    /// Locks the mutable tuning properties. Called by the pre-validation runner
    /// before any entity is validated so a concurrent mutation from another thread
    /// is rejected fast rather than producing mid-batch divergence.
    /// </summary>
    internal void Freeze() => _frozen = true;

    private void ThrowIfFrozen()
    {
        if (_frozen)
        {
            throw new InvalidOperationException(
                "ValidationOptions cannot be mutated after the options object has reached a Winnow operation. " +
                "Configure FailureBehavior and CancellationCheckInterval before passing the options to Insert/Update/Delete/Upsert.");
        }
    }
}
