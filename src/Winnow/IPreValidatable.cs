namespace Winnow;

/// <summary>
/// Shared pre-validation surface for the three operation interfaces
/// (<see cref="IOperation{TEntity, TKey}"/>,
/// <see cref="IInsertOperation{TEntity, TKey}"/>,
/// <see cref="IUpsertOperation{TEntity, TKey}"/>). The accumulator type and
/// <c>ApplyPreValidation</c> return shape differ per operation, so those
/// members live on the leaf interfaces; this base captures only the
/// configuration carriers strategies read uniformly.
/// </summary>
internal interface IPreValidatable<TEntity, TKey>
    where TEntity : class
    where TKey : notnull, IEquatable<TKey>
{
    /// <summary>The configured pre-validation options (or null if none).</summary>
    ValidationOptions? Validation { get; }

    /// <summary>
    /// Optional navigation filter forwarded to the pre-validation navigation walk
    /// so excluded navigations are not validated, matching the scope of the graph
    /// operation. Always <c>null</c> for flat operations.
    /// </summary>
    NavigationFilter? NavigationFilter => null;
}
