namespace Winnow;

/// <summary>
/// Machine-readable codes that Winnow's built-in pre-validation pipeline assigns
/// to <see cref="ValidationError.Code"/>. Compare against these constants when
/// switching on the source of a failure rather than hardcoding the underlying
/// strings; the strings themselves are stable but the constants give IDE
/// support and a single point of truth.
/// </summary>
/// <remarks>
/// Errors emitted by property-level <see cref="System.ComponentModel.DataAnnotations.ValidationAttribute"/>s
/// use the attribute's CLR type name (for example <c>"RequiredAttribute"</c> or
/// <c>"RangeAttribute"</c>) — those are not listed here because they come from
/// the framework, not from Winnow.
/// </remarks>
public static class ValidationErrorCodes
{
    /// <summary>
    /// Emitted for an entity-level rule reported by
    /// <see cref="System.ComponentModel.DataAnnotations.IValidatableObject.Validate"/>,
    /// to distinguish such failures from property-attribute failures.
    /// </summary>
    public const string ValidatableObject = "WINNOW_VALIDATABLE_OBJECT";

    /// <summary>
    /// Emitted by the navigation walker when it reaches
    /// <see cref="GraphValidationOptions.MaxNavigationDepth"/> and stops
    /// descending. Treat this as a configuration signal — raise the cap
    /// or narrow the graph — rather than a data-quality problem.
    /// </summary>
    public const string NavigationDepthLimit = "WINNOW_NAV_DEPTH_LIMIT";

    /// <summary>
    /// Emitted when a <c>null</c> entity reference appears in the input batch.
    /// </summary>
    public const string NullEntity = "WINNOW_NULL_ENTITY";
}
