namespace Winnow;

/// <summary>
/// A single validation problem detected on an entity by a pre-validation pipeline.
/// </summary>
/// <param name="PropertyName">
/// The property the failure is attached to. Use an empty string for entity-level
/// failures (cross-field rules that do not belong to a single property).
/// </param>
/// <param name="Message">Human-readable description of the failure.</param>
/// <param name="Code">
/// Optional machine-readable code (for example <c>"REQUIRED"</c> or
/// <c>"RANGE"</c>) callers can switch on when localising or grouping failures.
/// </param>
public readonly record struct ValidationError(string PropertyName, string Message, string? Code = null);
