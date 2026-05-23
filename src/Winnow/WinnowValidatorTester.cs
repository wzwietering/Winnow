namespace Winnow;

/// <summary>
/// Test-time helper for exercising a <see cref="WinnowValidator{TEntity}"/>
/// outside the pre-validation pipeline. Encapsulates the <c>using</c>-discipline
/// required by <see cref="ValidationCollector"/> so unit tests do not leak the
/// rented <see cref="System.Buffers.ArrayPool{T}"/> buffer when a validator
/// emits more than <see cref="ValidationCollector"/>'s inline capacity.
/// </summary>
/// <remarks>
/// Prefer this over a hand-rolled <c>ValidationCollector.Create()</c> call in
/// tests: the collector is a <c>ref struct</c>, so the compiler cannot enforce
/// disposal — getting the <c>using</c> wrong silently leaks pool memory across
/// the test run.
/// </remarks>
public static class WinnowValidatorTester
{
    /// <summary>
    /// Runs <paramref name="validator"/> against <paramref name="entity"/> and
    /// returns the recorded errors as a snapshot. The underlying collector is
    /// disposed before this method returns; the returned list is safe to keep.
    /// </summary>
    public static IReadOnlyList<ValidationError> Validate<TEntity>(
        WinnowValidator<TEntity> validator, TEntity entity)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(validator);
        var collector = ValidationCollector.Create();
        try
        {
            validator(entity, ref collector);
            return collector.AsSpan().ToArray();
        }
        finally
        {
            collector.Dispose();
        }
    }
}
