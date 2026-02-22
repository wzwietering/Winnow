using System.Linq.Expressions;

namespace Winnow;

/// <summary>
/// Mutable fluent builder for creating immutable <see cref="NavigationFilter"/> instances.
/// Use <see cref="NavigationFilter.Include"/> or <see cref="NavigationFilter.Exclude"/> to start building.
/// <para><strong>Thread safety:</strong> This builder is NOT thread-safe.
/// Create and build filters on a single thread. The resulting
/// <see cref="NavigationFilter"/> is immutable and thread-safe.</para>
/// </summary>
public sealed class NavigationFilterBuilder
{
    private readonly Dictionary<Type, HashSet<string>> _rules = [];
    private readonly bool _isIncludeMode;

    internal NavigationFilterBuilder(bool isIncludeMode) => _isIncludeMode = isIncludeMode;

    /// <summary>
    /// Adds a navigation property to the filter for the specified entity type.
    /// </summary>
    public NavigationFilterBuilder Navigation<TEntity>(
        Expression<Func<TEntity, object?>> navigationExpression)
        where TEntity : class
    {
        var name = ExtractPropertyName(navigationExpression);

        if (!_rules.TryGetValue(typeof(TEntity), out var set))
        {
            set = [];
            _rules[typeof(TEntity)] = set;
        }

        set.Add(name);
        return this;
    }

    /// <summary>
    /// Adds multiple navigation properties for a single entity type.
    /// </summary>
    public NavigationFilterBuilder Navigations<TEntity>(
        params Expression<Func<TEntity, object?>>[] navigationExpressions)
        where TEntity : class
    {
        foreach (var expression in navigationExpressions)
        {
            Navigation(expression);
        }
        return this;
    }

    /// <summary>
    /// Builds an immutable <see cref="NavigationFilter"/> from the configured rules.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no navigation rules have been configured. An empty filter would be
    /// semantically ambiguous (include-nothing vs exclude-nothing) and likely indicates
    /// a configuration error.
    /// </exception>
    public NavigationFilter Build()
    {
        if (_rules.Count == 0)
        {
            throw new InvalidOperationException(
                "NavigationFilter has no rules configured. " +
                "Add navigation rules with .Navigation<T>().");
        }

        var immutableRules = _rules.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlySet<string>)kvp.Value.ToHashSet());
        return new NavigationFilter(immutableRules, _isIncludeMode);
    }

    /// <summary>
    /// Implicitly converts a builder to a <see cref="NavigationFilter"/> by calling <see cref="Build"/>.
    /// <para><strong>Warning:</strong> Throws <see cref="InvalidOperationException"/> if no rules
    /// have been configured via <see cref="Navigation{TEntity}"/>.</para>
    /// </summary>
    public static implicit operator NavigationFilter(NavigationFilterBuilder builder) =>
        builder.Build();

    private static string ExtractPropertyName<TEntity>(
        Expression<Func<TEntity, object?>> expression)
    {
        var body = expression.Body;

        if (body is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
        {
            body = unary.Operand;
        }

        return body is MemberExpression member
            ? member.Member.Name
            : throw new ArgumentException(
                "Expression must be a simple property access (e.g., e => e.PropertyName). " +
                "Method calls, nested properties, and complex expressions are not supported.",
                nameof(expression));
    }
}
