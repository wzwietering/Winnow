using System.ComponentModel;

namespace Winnow;

/// <summary>
/// Thrown by the pre-validation pipeline when
/// <see cref="ValidationOptions.FailureBehavior"/> is
/// <see cref="ValidationFailureBehavior.ThrowAfterBatch"/> and one or more entities
/// fail validation. Carries the aggregated per-entity failures so callers can
/// react to them without re-running the validator.
/// </summary>
/// <remarks>
/// Named with the <c>Winnow</c> prefix to avoid collision with
/// <see cref="System.ComponentModel.DataAnnotations.ValidationException"/>.
/// </remarks>
public sealed class WinnowValidationException : Exception
{
    private const int MaxIndicesInMessage = 5;

    /// <summary>
    /// The validation failures that triggered this exception, keyed by the entity's
    /// position in the original input batch. Non-empty when constructed via the
    /// failures-list overload; empty when the exception was constructed via the
    /// message-only or message-plus-inner overloads (use those for re-throw scenarios).
    /// </summary>
    public IReadOnlyList<EntityFailure> Failures { get; }

    /// <summary>
    /// Creates an exception carrying the supplied per-entity failures. Throws
    /// <see cref="ArgumentException"/> if the list is empty — a validation
    /// exception without failures from the pipeline is semantically incoherent.
    /// </summary>
    public WinnowValidationException(IReadOnlyList<EntityFailure> failures)
        : base(BuildMessage(RequireFailures(failures)))
    {
        Failures = failures;
    }

    /// <summary>
    /// Parameterless constructor for serialization scenarios and frameworks that
    /// require it. <see cref="Failures"/> is initialised to an empty list.
    /// </summary>
    public WinnowValidationException()
    {
        Failures = Array.Empty<EntityFailure>();
    }

    /// <summary>
    /// Creates an exception with a custom message and no per-entity failures.
    /// Intended for wrapping/rethrow scenarios where the failure detail is not
    /// available at the rethrow site.
    /// </summary>
    public WinnowValidationException(string message)
        : base(message)
    {
        Failures = Array.Empty<EntityFailure>();
    }

    /// <summary>
    /// Creates an exception with a custom message and an inner exception, with no
    /// per-entity failures. Intended for wrapping/rethrow scenarios.
    /// </summary>
    public WinnowValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Failures = Array.Empty<EntityFailure>();
    }

    private static IReadOnlyList<EntityFailure> RequireFailures(IReadOnlyList<EntityFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        if (failures.Count == 0)
        {
            throw new ArgumentException(
                "WinnowValidationException requires at least one failure; an empty list is semantically incoherent.",
                nameof(failures));
        }
        return failures;
    }

    private static string BuildMessage(IReadOnlyList<EntityFailure> failures)
    {
        if (failures.Count == 1)
        {
            var single = failures[0];
            return $"Pre-validation failed for entity at index {single.EntityIndex}: {single.Message}";
        }
        return $"Pre-validation failed for {failures.Count} entities at {FormatIndices(failures)}.";
    }

    private static string FormatIndices(IReadOnlyList<EntityFailure> failures)
    {
        var take = Math.Min(failures.Count, MaxIndicesInMessage);
        var indices = new string[take];
        for (int i = 0; i < take; i++)
        {
            indices[i] = failures[i].EntityIndex.ToString();
        }
        var joined = $"indices {string.Join(", ", indices)}";
        return failures.Count > MaxIndicesInMessage ? joined + ", ..." : joined;
    }

    /// <summary>
    /// A snapshot of one entity's pre-validation failure, surfaced via
    /// <see cref="WinnowValidationException.Failures"/>. Drive UI / API
    /// responses off <see cref="Errors"/> — the structured per-property list —
    /// rather than parsing <see cref="Message"/>, which is a debug-only
    /// concatenation whose exact format is not part of the API contract.
    /// </summary>
    /// <param name="EntityIndex">Zero-based position in the original input batch.</param>
    /// <param name="Message">
    /// Human-readable summary built from <paramref name="Errors"/>. Format is intentionally
    /// underspecified and may change between minor releases. Hidden from IntelliSense
    /// to steer callers to <paramref name="Errors"/>; still accessible programmatically
    /// for logging.
    /// </param>
    /// <param name="Errors">Structured per-property errors recorded by the validator.</param>
    public sealed record EntityFailure(
        int EntityIndex,
        [property: EditorBrowsable(EditorBrowsableState.Never)] string Message,
        IReadOnlyList<ValidationError> Errors);
}
