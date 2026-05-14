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
    /// primary-key default-value check. Use this overload when binding <typeparamref name="TKey"/>
    /// explicitly is useful (e.g. storing the expression in a typed variable). For composite
    /// match keys and most other uses, prefer the <see cref="WithMatchBy{TEntity}(UpsertOptions, Expression{Func{TEntity, object}})"/>
    /// single-type-argument overload — it accepts both simple and anonymous projections without
    /// requiring <c>TKey = object</c>.
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
        options.MatchBy = new MatchByConfiguration(matchExpression);
        return options;
    }

    /// <summary>
    /// Configures upsert to route entities by a business-key expression instead of the
    /// primary-key default-value check. Preferred overload for both simple and composite
    /// match keys: <c>options.WithMatchBy&lt;Product&gt;(p =&gt; p.Sku)</c> for a single property,
    /// <c>options.WithMatchBy&lt;Order&gt;(o =&gt; new { o.TenantId, o.ExternalId })</c> for a
    /// composite. Avoids the awkward <c>TKey = object</c> spelling required by the
    /// two-type-argument overload when supplying an anonymous projection.
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
        options.MatchBy = new MatchByConfiguration(matchExpression);
        return options;
    }
}
