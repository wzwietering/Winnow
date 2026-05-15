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
    /// After validating every entity in the batch, throw a
    /// <see cref="WinnowValidationException"/> aggregating all failures. Valid
    /// entities in the same batch are not sent to the database — the throw
    /// pre-empts the entire round trip. Use when "any failure aborts the batch"
    /// is the right contract; if you want partial progress, use
    /// <see cref="RecordAsFailure"/> instead. The name disambiguates from a
    /// hypothetical fail-on-first mode, which would short-circuit the scan.
    /// </summary>
    ThrowAfterBatch = 1,
}
