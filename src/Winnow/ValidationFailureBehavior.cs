namespace Winnow;

/// <summary>
/// Controls what happens when pre-validation reports failures on at least one entity.
/// </summary>
public enum ValidationFailureBehavior
{
    /// <summary>
    /// Record each invalid entity as a failure with
    /// <see cref="FailureReason.PreValidationError"/> and continue processing the
    /// remaining valid entities. Default behaviour — matches the
    /// "winnow out the failures" model the rest of the library follows.
    /// </summary>
    RecordAsFailure = 0,

    /// <summary>
    /// After validating every entity in the batch, throw a
    /// <see cref="WinnowValidationException"/> aggregating all failures. Valid
    /// entities in the same batch are not sent to the database — the throw
    /// pre-empts the entire round trip. The scan is not short-circuited on the
    /// first failure: the exception carries every offending entity so callers
    /// can react to them all without re-running the validator. Use when "any
    /// failure aborts the batch" is the right contract; if you want partial
    /// progress, use <see cref="RecordAsFailure"/> instead.
    /// </summary>
    Throw = 1,
}
