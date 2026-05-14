namespace Winnow;

/// <summary>
/// Configuration for the optional pre-validation pipeline. When attached to
/// <see cref="WinnowOptions.Validation"/> or <see cref="GraphOptionsBase.Validation"/>,
/// entities are validated in-process before any database round trip — invalid
/// entities are recorded as failures with <see cref="FailureReason.ValidationError"/>
/// and never sent to the strategy.
/// </summary>
/// <remarks>
/// Construct via the <c>WithValidation</c> or <c>WithDataAnnotations</c> extension
/// methods rather than directly — those carry the type information needed to wire
/// the validator to a specific <c>TEntity</c>.
/// </remarks>
public sealed class ValidationOptions
{
    private int _cancellationCheckInterval = DefaultCancellationCheckInterval;

    /// <summary>
    /// Default cancellation poll interval — every 256 entities. Picked to keep the
    /// volatile read out of the hottest validator inner loop while still bounding
    /// time-to-cancel to a small fraction of a millisecond on typical hardware.
    /// </summary>
    public const int DefaultCancellationCheckInterval = 256;

    /// <summary>
    /// The entity type the configured validator applies to. Set by the
    /// <c>WithValidation</c> / <c>WithDataAnnotations</c> extension methods so the
    /// pipeline can fail fast if the carrier is attached to options targeting a
    /// different entity type.
    /// </summary>
    internal Type EntityType { get; }

    /// <summary>
    /// Type-erased validator delegate. The concrete type is
    /// <see cref="ValidatorDelegate{TEntity}"/> for <see cref="EntityType"/>;
    /// the pipeline casts it once per batch and caches the typed reference.
    /// </summary>
    internal object Validator { get; }

    /// <summary>
    /// True when the validator was built by the DataAnnotations adapter
    /// (<c>WithDataAnnotations</c>). Only DataAnnotations validators can be applied
    /// polymorphically to child entities, so this is the gate for
    /// <see cref="IncludeNavigations"/>.
    /// </summary>
    internal bool IsDataAnnotationsValidator { get; }

    /// <summary>
    /// Controls what happens when at least one entity fails validation.
    /// Default: <see cref="ValidationFailureBehavior.RecordAsFailure"/>.
    /// </summary>
    public ValidationFailureBehavior FailureBehavior { get; set; } = ValidationFailureBehavior.RecordAsFailure;

    /// <summary>
    /// Reserved for graph operations: when set to <c>true</c>, pre-validation
    /// descends into the navigation properties that the configured
    /// <see cref="GraphOptionsBase.NavigationFilter"/> traverses and validates each
    /// reachable entity. Default: <c>false</c> — only the top-level entities are
    /// validated. Has no effect on flat (non-graph) operations.
    /// </summary>
    public bool IncludeNavigations { get; set; }

    /// <summary>
    /// How often the validation pipeline checks the cancellation token, measured
    /// in entities. Default: <see cref="DefaultCancellationCheckInterval"/>.
    /// Must be positive. Lower values give faster cancellation response at a small
    /// throughput cost; higher values trade latency for throughput.
    /// </summary>
    public int CancellationCheckInterval
    {
        get => _cancellationCheckInterval;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _cancellationCheckInterval = value;
        }
    }

    internal ValidationOptions(Type entityType, object validator, bool isDataAnnotationsValidator = false)
    {
        EntityType = entityType;
        Validator = validator;
        IsDataAnnotationsValidator = isDataAnnotationsValidator;
    }
}
