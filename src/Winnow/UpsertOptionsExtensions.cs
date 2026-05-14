using System.Linq.Expressions;
using Winnow.Internal;

namespace Winnow;

/// <summary>
/// Fluent helpers for <see cref="UpsertOptions"/>.
/// </summary>
public static class UpsertOptionsExtensions
{
    /// <summary>
    /// Configures upsert to route entities by a business-key expression instead of the
    /// primary-key default-value check. Accepts both single-property selectors
    /// (<c>p =&gt; p.Sku</c>) and anonymous projections for composite match keys
    /// (<c>o =&gt; new { o.TenantId, o.ExternalId }</c>). Callers that need to bind the
    /// expression to a typed variable can declare
    /// <c>Expression&lt;Func&lt;TEntity, TKey&gt;&gt; expr = ...</c> and pass via the
    /// implicit conversion to <see cref="Expression{Func}"/>.
    /// Calling this method more than once on the same options instance replaces the
    /// previously configured expression — the last call wins.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown immediately when the expression shape is unsupported (method calls, nested
    /// member access, complex projections). Property mapping is verified later, when the
    /// upsert runs against the configured <see cref="Microsoft.EntityFrameworkCore.DbContext"/>.
    /// </exception>
    public static UpsertOptions WithMatchBy<TEntity>(
        this UpsertOptions options,
        Expression<Func<TEntity, object?>> matchExpression)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(matchExpression);
        MatchExpressionParser.ValidateShape<TEntity>(matchExpression);
        options.MatchBy = new MatchByConfiguration(matchExpression);
        return options;
    }
}
