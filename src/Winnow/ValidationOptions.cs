namespace Winnow;

/// <summary>
/// Configuration for the optional pre-validation pipeline. When attached to
/// <see cref="WinnowOptions.Validation"/> or <see cref="GraphOptionsBase.Validation"/>,
/// entities are validated in-process before any database round trip — invalid
/// entities are recorded as failures with <see cref="FailureReason.ValidationError"/>
/// and never sent to the strategy.
/// </summary>
/// <remarks>
/// <para>
/// Construct via the <c>WithValidation</c> or <c>WithDataAnnotations</c> extension
/// methods rather than directly — those carry the type information needed to wire
/// the validator to a specific <c>TEntity</c>.
/// </para>
/// <para>
/// This type is not designed for external subclassing. The constructor is
/// <c>private protected</c>, so the only subclass is the library-supplied
/// <see cref="GraphValidationOptions"/>. Treat the type as effectively sealed.
/// </para>
/// </remarks>
public class ValidationOptions
{
    private int _cancellationCheckInterval = DefaultCancellationCheckInterval;

    /// <summary>
    /// Default cancellation poll interval — every 256 entities. Picked to keep the
    /// volatile read out of the hottest validator inner loop while still bounding
    /// time-to-cancel to a small fraction of a millisecond on typical hardware.
    /// Exposed as <c>static readonly</c> rather than <c>const</c> so a future
    /// tuning is observed by already-compiled consumer assemblies.
    /// </summary>
    public static readonly int DefaultCancellationCheckInterval = 256;

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
    /// polymorphically to child entities, so this gates navigation walking on
    /// <see cref="GraphValidationOptions"/>.
    /// </summary>
    internal bool IsDataAnnotationsValidator { get; }

    /// <summary>
    /// Whether the pipeline should descend into navigation properties when
    /// validating each entity. <c>false</c> on the base type — only
    /// <see cref="GraphValidationOptions"/> can enable navigation walking,
    /// which keeps the flat-vs-graph distinction enforced in the type system.
    /// </summary>
    internal virtual bool ShouldWalkNavigations => false;

    /// <summary>
    /// Maximum depth the navigation walker descends before stopping. Reached
    /// only when <see cref="ShouldWalkNavigations"/> is true. The base value is
    /// unused; <see cref="GraphValidationOptions"/> overrides it with the
    /// user-configurable depth cap.
    /// </summary>
    internal virtual int NavigationDepthLimit => 0;

    /// <summary>
    /// Controls what happens when at least one entity fails validation.
    /// Default: <see cref="ValidationFailureBehavior.RecordAsFailure"/>.
    /// </summary>
    public ValidationFailureBehavior FailureBehavior { get; set; } = ValidationFailureBehavior.RecordAsFailure;

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

    private protected ValidationOptions(Type entityType, object validator, bool isDataAnnotationsValidator)
    {
        EntityType = entityType;
        Validator = validator;
        IsDataAnnotationsValidator = isDataAnnotationsValidator;
    }

    internal static ValidationOptions CreateFlat(Type entityType, object validator, bool isDataAnnotationsValidator = false) =>
        new(entityType, validator, isDataAnnotationsValidator);
}
