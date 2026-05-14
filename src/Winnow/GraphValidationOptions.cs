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
/// Direct construction is supported for adapter authors and tests; production
/// callers should prefer the extension methods.
/// </remarks>
public sealed class GraphValidationOptions : ValidationOptions
{
    /// <summary>
    /// Default depth cap for <see cref="MaxNavigationDepth"/>. Picked to match the
    /// recursion-depth budget commonly used by EF Core graph traversals — deep
    /// enough for any realistic entity tree, shallow enough to surface a
    /// configuration error rather than a process-terminating <c>StackOverflowException</c>.
    /// </summary>
    public const int DefaultMaxNavigationDepth = 32;

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
    /// <c>WithDataAnnotations</c>. Custom <see cref="ValidatorDelegate{TEntity}"/>
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
                    "Typed ValidatorDelegate<TEntity> cannot be applied polymorphically to children " +
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
    /// <see cref="DefaultMaxNavigationDepth"/>. Must be positive.
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

    internal override bool ShouldWalkNavigations => _includeNavigations;
    internal override int NavigationDepthLimit => _maxNavigationDepth;

    /// <summary>
    /// Direct construction is supported for adapter authors and tests; production
    /// callers should prefer the <c>WithValidation</c> / <c>WithDataAnnotations</c>
    /// extension methods on a <see cref="GraphOptionsBase"/> subtype, which wire
    /// the validator and entity type for you.
    /// </summary>
    public GraphValidationOptions(Type entityType, object validator, bool isDataAnnotationsValidator = false)
        : base(entityType, validator, isDataAnnotationsValidator)
    {
    }
}
