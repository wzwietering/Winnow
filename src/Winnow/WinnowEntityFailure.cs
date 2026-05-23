using System.ComponentModel;

namespace Winnow;

/// <summary>
/// A snapshot of one entity's pre-validation failure, surfaced via
/// <see cref="WinnowValidationException.Failures"/>. Drive UI / API responses
/// off <see cref="Errors"/> — the structured per-property list — rather than
/// parsing <see cref="Message"/>, which is a debug-only concatenation whose
/// exact format is not part of the API contract.
/// </summary>
/// <param name="EntityIndex">Zero-based position in the original input batch.</param>
/// <param name="Message">
/// Human-readable summary built from <paramref name="Errors"/>. Format is
/// intentionally underspecified and may change between minor releases. Hidden
/// from IntelliSense to steer callers to <paramref name="Errors"/>; still
/// accessible programmatically for logging.
/// </param>
/// <param name="Errors">Structured per-property errors recorded by the validator.</param>
public sealed record WinnowEntityFailure(
    int EntityIndex,
    [property: EditorBrowsable(EditorBrowsableState.Never)] string Message,
    IReadOnlyList<ValidationError> Errors);
