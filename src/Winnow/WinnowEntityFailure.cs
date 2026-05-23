using System.ComponentModel;

namespace Winnow;

/// <summary>
/// A snapshot of one entity's pre-validation failure, surfaced via
/// <see cref="WinnowValidationException.Failures"/>. Drive UI / API responses
/// off <see cref="ValidationErrors"/> — the structured per-property list —
/// rather than parsing <see cref="ErrorMessage"/>, which is a debug-only
/// concatenation whose exact format is not part of the API contract.
/// </summary>
/// <remarks>
/// This type is intentionally narrower than the operation-shaped failure types
/// (<c>InsertFailure</c>, <c>WinnowFailure&lt;TKey&gt;</c>, <c>UpsertFailure&lt;TKey&gt;</c>)
/// because the <see cref="ValidationFailureBehavior.Throw"/> path runs before any
/// database attempt — there is no <c>TKey</c> available yet and no operation context
/// to attach. The result-list path (<see cref="ValidationFailureBehavior.RecordAsFailure"/>)
/// surfaces failures via the operation-specific types instead. See the
/// "Consuming validation failures" section of <c>docs/pre-validation.md</c>.
/// </remarks>
/// <param name="EntityIndex">Zero-based position in the original input batch.</param>
/// <param name="ValidationErrors">Structured per-property errors recorded by the validator.</param>
/// <param name="ErrorMessage">
/// Human-readable summary built from <paramref name="ValidationErrors"/>. Format
/// is intentionally underspecified and may change between minor releases.
/// Hidden from IntelliSense to steer callers to <paramref name="ValidationErrors"/>;
/// still accessible programmatically for logging.
/// </param>
public sealed record WinnowEntityFailure(
    int EntityIndex,
    IReadOnlyList<ValidationError> ValidationErrors,
    [property: EditorBrowsable(EditorBrowsableState.Never)] string ErrorMessage);
