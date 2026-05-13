using System.Linq.Expressions;

namespace Winnow;

/// <summary>
/// Fluent helpers for <see cref="UpsertOptions"/>.
/// </summary>
public static class UpsertOptionsExtensions
{
    /// <summary>
    /// Sets <see cref="UpsertOptions.MatchBy"/> using a strongly-typed expression.
    /// Use a single property (<c>p =&gt; p.Sku</c>) or anonymous projection
    /// (<c>p =&gt; new { p.TenantId, p.ExternalId }</c>) for composite match keys.
    /// </summary>
    public static UpsertOptions WithMatchBy<TEntity, TKey>(
        this UpsertOptions options,
        Expression<Func<TEntity, TKey>> matchExpression)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(matchExpression);
        options.MatchBy = matchExpression;
        return options;
    }
}
