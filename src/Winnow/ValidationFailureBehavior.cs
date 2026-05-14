namespace Winnow;

/// <summary>
/// Controls what happens when pre-validation reports failures on at least one entity.
/// </summary>
public enum ValidationFailureBehavior
{
    /// <summary>
    /// Record each invalid entity as a failure with
    /// <see cref="FailureReason.ValidationError"/> and continue processing the
    /// remaining valid entities. Default behaviour — matches the
    /// "winnow out the failures" model the rest of the library follows.
    /// </summary>
    RecordAsFailure = 0,

    /// <summary>
    /// Throw a <see cref="ValidationException"/> as soon as the validator finishes
    /// running across the batch and any failures were reported. The exception
    /// carries the aggregated failures so callers can inspect them. No database
    /// round trips occur in this mode when any entity is invalid.
    /// </summary>
    Throw = 1,
}
