namespace Winnow;

/// <summary>
/// Thrown by the pre-validation pipeline when
/// <see cref="ValidationOptions.FailureBehavior"/> is
/// <see cref="ValidationFailureBehavior.Throw"/> and one or more entities fail
/// validation. Carries the aggregated per-entity failures so callers can react
/// to them without re-running the validator.
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
    /// position in the original input batch. Always non-empty: the pipeline only
    /// throws this exception when at least one failure has been collected.
    /// </summary>
    public IReadOnlyList<EntityValidationFailure> Failures { get; }

    /// <summary>
    /// Creates an exception carrying the supplied per-entity failures. Throws
    /// <see cref="ArgumentException"/> if the list is empty — a validation
    /// exception without failures is semantically incoherent.
    /// </summary>
    public WinnowValidationException(IReadOnlyList<EntityValidationFailure> failures)
        : base(BuildMessage(RequireFailures(failures)))
    {
        Failures = failures;
    }

    private static IReadOnlyList<EntityValidationFailure> RequireFailures(IReadOnlyList<EntityValidationFailure> failures)
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

    private static string BuildMessage(IReadOnlyList<EntityValidationFailure> failures)
    {
        if (failures.Count == 1)
        {
            var single = failures[0];
            return $"Pre-validation failed for entity at index {single.EntityIndex}: {single.Message}";
        }
        return $"Pre-validation failed for {failures.Count} entities at {FormatIndices(failures)}.";
    }

    private static string FormatIndices(IReadOnlyList<EntityValidationFailure> failures)
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
}

/// <summary>
/// A snapshot of one entity's pre-validation failure, surfaced via
/// <see cref="WinnowValidationException.Failures"/>.
/// </summary>
public sealed record EntityValidationFailure(int EntityIndex, string Message, IReadOnlyList<ValidationError> Errors);
