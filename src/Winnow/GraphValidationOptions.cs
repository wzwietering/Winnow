namespace Winnow;

/// <summary>
/// Graph-only pre-validation options. Exposes <see cref="IncludeNavigations"/>
/// on top of <see cref="ValidationOptions"/> so the navigation walk is only
/// configurable on graph operations, where it has well-defined semantics.
/// </summary>
/// <remarks>
/// Construct via the <c>WithValidation</c> or <c>WithDataAnnotations</c> extension
/// methods on a <see cref="GraphOptionsBase"/> subtype (e.g. <see cref="InsertGraphOptions"/>).
/// Flat operations cannot accept this type through the public API — their
/// <c>Validation</c> property is typed as <see cref="ValidationOptions"/>.
/// </remarks>
public sealed class GraphValidationOptions : ValidationOptions
{
    /// <summary>
    /// When set to <c>true</c>, pre-validation descends into navigation properties
    /// and validates each reachable entity (DataAnnotations only). Cycle protection
    /// is reference-based; failures are surfaced on the top-level entity with a
    /// property path locating the offending child. The walk honours
    /// <see cref="GraphOptionsBase.NavigationFilter"/> — excluded navigations are
    /// skipped. Default: <c>false</c> — only the top-level entities are validated.
    /// </summary>
    public bool IncludeNavigations { get; set; }

    internal GraphValidationOptions(Type entityType, object validator, bool isDataAnnotationsValidator = false)
        : base(entityType, validator, isDataAnnotationsValidator)
    {
    }
}
