using Microsoft.EntityFrameworkCore.Metadata;

namespace Winnow.Internal;

/// <summary>
/// Parsed and validated representation of a user-supplied
/// <see cref="UpsertOptions.MatchBy"/> expression for a specific entity type.
/// </summary>
/// <typeparam name="TEntity">The entity type the expression targets.</typeparam>
internal sealed class MatchExpressionPlan<TEntity> where TEntity : class
{
    internal IReadOnlyList<IProperty> Properties { get; }
    internal Func<TEntity, object?[]> Extractor { get; }

    internal MatchExpressionPlan(
        IReadOnlyList<IProperty> properties,
        Func<TEntity, object?[]> extractor)
    {
        Properties = properties;
        Extractor = extractor;
    }
}
