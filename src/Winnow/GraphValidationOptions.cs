namespace Winnow;

/// <summary>
/// Graph-only pre-validation options. Exposes <see cref="IncludeNavigations"/>
/// and <see cref="MaxNavigationDepth"/> on top of <see cref="ValidationOptions"/>
/// so the navigation walk is only configurable on graph operations, where it
/// has well-defined semantics.
/// </summary>
/// <remarks>
/// Construct via the <c>WithValidation</c> or <c>WithDataAnnotations</c> extension
/// methods on a <see cref="GraphOptionsBase"/> subtype (e.g. <see cref="InsertGraphOptions"/>).
/// Flat operations cannot accept this type through the public API — their
/// <c>Validation</c> property is typed as <see cref="ValidationOptions"/>.
/// Adapter authors who need to construct an instance directly should use the
/// typed <see cref="Create{TEntity}(WinnowValidator{TEntity}, bool)"/> factory,
/// which preserves the entity-type ↔ validator-type invariant the pipeline relies on.
/// </remarks>
public sealed class GraphValidationOptions : ValidationOptions
{
    /// <summary>
    /// Default depth cap for <see cref="MaxNavigationDepth"/>. Internal so the
    /// value is not part of the public API surface; the JIT can fold it as a
    /// compile-time constant inside the library.
    /// </summary>
    internal const int DefaultMaxNavigationDepth = 32;

    private bool _includeNavigations;
    private int _maxNavigationDepth = DefaultMaxNavigationDepth;

    /// <summary>
    /// When set to <c>true</c>, pre-validation descends into navigation properties
    /// and validates each reachable entity (DataAnnotations only). Cycle protection
    /// is reference-based; failures are surfaced on the top-level entity with a
    /// property path locating the offending child. The walk honours
    /// <see cref="GraphOptionsBase.NavigationFilter"/> — excluded navigations are
    /// skipped. Default: <c>false</c> — only the top-level entities are validated.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when set to <c>true</c> on options that were not built by
    /// <c>WithDataAnnotations</c>. Custom <see cref="WinnowValidator{TEntity}"/>
    /// validators cannot be applied polymorphically to children of differing
    /// types, so this combination is rejected eagerly at configuration time
    /// rather than at execution.
    /// </exception>
    public bool IncludeNavigations
    {
        get => _includeNavigations;
        set
        {
            if (value && !IsDataAnnotationsValidator)
            {
                throw new InvalidOperationException(
                    "IncludeNavigations only supports validators built by WithDataAnnotations. " +
                    "Typed WinnowValidator<TEntity> cannot be applied polymorphically to children " +
                    "of differing types — wire WithDataAnnotations<TEntity>() instead, or leave " +
                    "IncludeNavigations = false and validate children with a separate options instance.");
            }
            _includeNavigations = value;
        }
    }

    /// <summary>
    /// Maximum recursion depth for the navigation walk before the walker stops
    /// descending further into child entities. When the cap is reached, a
    /// <see cref="ValidationError"/> with code <c>WINNOW_NAV_DEPTH_LIMIT</c> is
    /// recorded on the property path where the walk stopped. Default:
    /// <c>32</c>. Must be positive.
    /// </summary>
    public int MaxNavigationDepth
    {
        get => _maxNavigationDepth;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxNavigationDepth = value;
        }
    }

    private GraphValidationOptions(Type entityType, object validator, bool isDataAnnotationsValidator)
        : base(entityType, validator, isDataAnnotationsValidator)
    {
    }

    /// <summary>
    /// Typed factory for in-assembly callers (extension methods, tests).
    /// External code should always go through the <c>WithValidation</c> /
    /// <c>WithDataAnnotations</c> extension methods on a
    /// <see cref="GraphOptionsBase"/> subtype — the <c>Validation</c> setter is
    /// itself <c>internal</c>, so a directly-constructed instance can never be
    /// attached to options through the public surface.
    /// </summary>
    /// <typeparam name="TEntity">
    /// Entity type the validator applies to. The pipeline rejects mismatches at
    /// execution time; this factory enforces the link at construction time so
    /// the type can never drift.
    /// </typeparam>
    /// <param name="validator">The validator delegate.</param>
    /// <param name="isDataAnnotations">
    /// True only if the supplied delegate originates from
    /// <see cref="Internal.Validation.DataAnnotationsValidatorFactory"/>; gates
    /// <see cref="IncludeNavigations"/>. Defaults to <c>false</c> for callers
    /// who want a typed delegate.
    /// </param>
    internal static GraphValidationOptions Create<TEntity>(
        WinnowValidator<TEntity> validator,
        bool isDataAnnotations = false)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(validator);
        return new GraphValidationOptions(typeof(TEntity), validator, isDataAnnotations);
    }

    /// <summary>
    /// Internal escape hatch used by the DataAnnotations adapter, which builds
    /// the validator delegate from a runtime <see cref="Type"/> and so cannot
    /// reach the typed <see cref="Create{TEntity}"/> overload.
    /// </summary>
    internal static GraphValidationOptions CreateInternal(Type entityType, object validator, bool isDataAnnotationsValidator) =>
        new(entityType, validator, isDataAnnotationsValidator);
}
