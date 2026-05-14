namespace Winnow;

/// <summary>
/// Thrown by the pre-validation pipeline when
/// <see cref="ValidationOptions.FailureBehavior"/> is
/// <see cref="ValidationFailureBehavior.Throw"/> and one or more entities fail
/// validation. Carries the aggregated per-entity failures so callers can react
/// to them without re-running the validator.
/// </summary>
public sealed class ValidationException : Exception
{
    /// <summary>
    /// The validation failures that triggered this exception, keyed by the entity's
    /// position in the original input batch.
    /// </summary>
    public IReadOnlyList<EntityValidationFailure> Failures { get; }

    internal ValidationException(IReadOnlyList<EntityValidationFailure> failures)
        : base(BuildMessage(failures))
    {
        Failures = failures;
    }

    private static string BuildMessage(IReadOnlyList<EntityValidationFailure> failures)
    {
        if (failures.Count == 1)
        {
            var single = failures[0];
            return $"Pre-validation failed for entity at index {single.EntityIndex}: {single.Message}";
        }
        return $"Pre-validation failed for {failures.Count} entities.";
    }
}

/// <summary>
/// A snapshot of one entity's pre-validation failure, surfaced via
/// <see cref="ValidationException.Failures"/>.
/// </summary>
public sealed record EntityValidationFailure(int EntityIndex, string Message, IReadOnlyList<ValidationError> Errors);
