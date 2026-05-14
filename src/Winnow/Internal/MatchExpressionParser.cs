using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Winnow.Internal;

/// <summary>
/// Parses and validates a user-supplied <see cref="LambdaExpression"/> from
/// <see cref="UpsertOptions.MatchBy"/> into a runnable <see cref="MatchExpressionPlan{TEntity}"/>.
/// </summary>
internal static class MatchExpressionParser
{
    // Cache the parsed plan per (expression, entity type, TEntity) so a streaming workload
    // that reuses a single UpsertOptions doesn't pay Expression.Compile() cost on every batch.
    // Key uses reference equality of LambdaExpression — fluent helpers reuse the same
    // instance across calls, so cache hits are the normal case. typeof(TEntity) guards
    // against the latent class of bug where a caller passes a TEntity that doesn't match
    // the entityType, which would otherwise cast the cached plan to the wrong generic and NRE.
    private static readonly ConcurrentDictionary<(LambdaExpression, IEntityType, Type), object> PlanCache = new();

    internal static MatchExpressionPlan<TEntity> Parse<TEntity>(
        LambdaExpression expression,
        IEntityType entityType) where TEntity : class
    {
        var cached = (MatchExpressionPlan<TEntity>?)PlanCache.GetOrAdd(
            (expression, entityType, typeof(TEntity)),
            static key => BuildPlan<TEntity>((LambdaExpression)key.Item1, (IEntityType)key.Item2));
        return cached!;
    }

    private static MatchExpressionPlan<TEntity> BuildPlan<TEntity>(
        LambdaExpression expression, IEntityType entityType) where TEntity : class
    {
        ValidateLambdaShape<TEntity>(expression);
        var memberNames = ExtractMemberNames(expression);
        var properties = ResolveProperties<TEntity>(memberNames, entityType);
        var extractor = BuildExtractor<TEntity>(memberNames);
        return new MatchExpressionPlan<TEntity>(properties, extractor);
    }

    /// <summary>
    /// Runs only the model-independent shape checks (parameter shape + member access form).
    /// Used by <see cref="UpsertOptionsExtensions.WithMatchBy{TEntity, TKey}"/> to fail fast
    /// before <see cref="Parse{TEntity}"/> resolves properties against the EF Core model.
    /// </summary>
    internal static void ValidateShape<TEntity>(LambdaExpression expression) where TEntity : class
    {
        ValidateLambdaShape<TEntity>(expression);
        ExtractMemberNames(expression);
    }

    private static void ValidateLambdaShape<TEntity>(LambdaExpression expression)
    {
        if (expression.Parameters.Count != 1 || expression.Parameters[0].Type != typeof(TEntity))
        {
            throw new ArgumentException(
                $"MatchBy expression must take a single parameter of type {typeof(TEntity).Name}.",
                nameof(expression));
        }
    }

    private static IReadOnlyList<string> ExtractMemberNames(LambdaExpression expression)
    {
        var body = Unwrap(expression.Body);
        return body switch
        {
            MemberExpression member => [GetMemberName(member)],
            NewExpression newExpr => ExtractFromNewExpression(newExpr),
            _ => throw new ArgumentException(
                "MatchBy expression must be a simple property access (e => e.PropertyName) " +
                "or an anonymous projection (e => new { e.A, e.B }). " +
                "Method calls, nested properties, and complex expressions are not supported.",
                nameof(expression))
        };
    }

    private static Expression Unwrap(Expression expression) =>
        expression is UnaryExpression { NodeType: ExpressionType.Convert } u ? u.Operand : expression;

    private static string GetMemberName(MemberExpression member)
    {
        if (member.Expression is not ParameterExpression)
        {
            throw new ArgumentException(
                "MatchBy expression must reference a property directly on the entity parameter " +
                "(e => e.PropertyName). Nested member access is not supported.");
        }
        return member.Member.Name;
    }

    private static IReadOnlyList<string> ExtractFromNewExpression(NewExpression newExpr)
    {
        if (newExpr.Arguments.Count == 0)
        {
            throw new ArgumentException(
                "MatchBy anonymous projection must include at least one property " +
                "(e => new { e.A, e.B }).");
        }

        var names = new string[newExpr.Arguments.Count];
        for (var i = 0; i < newExpr.Arguments.Count; i++)
        {
            var arg = Unwrap(newExpr.Arguments[i]);
            if (arg is not MemberExpression member)
            {
                throw new ArgumentException(
                    "MatchBy anonymous projection members must be simple property access " +
                    "on the entity parameter (e => new { e.A, e.B }).");
            }
            names[i] = GetMemberName(member);
        }
        return names;
    }

    private static IReadOnlyList<IProperty> ResolveProperties<TEntity>(
        IReadOnlyList<string> names,
        IEntityType entityType) where TEntity : class
    {
        var properties = new IProperty[names.Count];
        for (var i = 0; i < names.Count; i++)
        {
            properties[i] = ResolveSingleProperty<TEntity>(names[i], entityType);
        }
        return properties;
    }

    private static IProperty ResolveSingleProperty<TEntity>(string name, IEntityType entityType)
        where TEntity : class
    {
        if (entityType.FindNavigation(name) != null)
        {
            throw new ArgumentException(
                $"MatchBy cannot reference navigation property '{name}' on entity " +
                $"{typeof(TEntity).Name}. Use the foreign-key column instead.");
        }

        var property = entityType.FindProperty(name)
            ?? throw new ArgumentException(
                $"MatchBy property '{name}' does not exist on entity type {typeof(TEntity).Name}.");

        if (property.IsPrimaryKey())
        {
            throw new ArgumentException(
                $"MatchBy property '{name}' is the primary key on {typeof(TEntity).Name}. " +
                "Omit MatchBy to use default key-value detection.");
        }

        if (property.ValueGenerated != ValueGenerated.Never)
        {
            throw new ArgumentException(
                $"MatchBy property '{name}' is store-generated on {typeof(TEntity).Name} and not safe to match on. " +
                "Choose a stable, application-supplied column.");
        }

        return property;
    }

    private static Func<TEntity, object?[]> BuildExtractor<TEntity>(IReadOnlyList<string> memberNames)
        where TEntity : class
    {
        var parameter = Expression.Parameter(typeof(TEntity), "e");
        var arrayItems = new Expression[memberNames.Count];
        for (var i = 0; i < memberNames.Count; i++)
        {
            var memberAccess = Expression.PropertyOrField(parameter, memberNames[i]);
            arrayItems[i] = Expression.Convert(memberAccess, typeof(object));
        }
        var newArray = Expression.NewArrayInit(typeof(object), arrayItems);
        return Expression.Lambda<Func<TEntity, object?[]>>(newArray, parameter).Compile();
    }
}
