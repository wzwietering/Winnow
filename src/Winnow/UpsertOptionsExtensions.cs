using System.Linq.Expressions;
using Winnow.Internal;

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
    /// <exception cref="ArgumentException">
    /// Thrown immediately when the expression shape is unsupported (method calls, nested
    /// member access, complex projections). Property mapping is verified later, when the
    /// upsert runs against the configured <see cref="Microsoft.EntityFrameworkCore.DbContext"/>.
    /// </exception>
    public static UpsertOptions WithMatchBy<TEntity, TKey>(
        this UpsertOptions options,
        Expression<Func<TEntity, TKey>> matchExpression)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(matchExpression);
        MatchExpressionParser.ValidateShape<TEntity>(matchExpression);
        options.MatchBy = matchExpression;
        return options;
    }

    /// <summary>
    /// Sets <see cref="UpsertOptions.MatchBy"/> using a strongly-typed expression.
    /// Single-type-argument overload — preferred for composite match keys, where the
    /// anonymous projection type cannot be named explicitly:
    /// <c>options.WithMatchBy&lt;Order&gt;(o =&gt; new { o.TenantId, o.ExternalId })</c>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown immediately when the expression shape is unsupported.
    /// </exception>
    public static UpsertOptions WithMatchBy<TEntity>(
        this UpsertOptions options,
        Expression<Func<TEntity, object>> matchExpression)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(matchExpression);
        MatchExpressionParser.ValidateShape<TEntity>(matchExpression);
        options.MatchBy = matchExpression;
        return options;
    }
}
