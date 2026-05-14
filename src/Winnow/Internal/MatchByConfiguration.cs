using System.Linq.Expressions;

namespace Winnow.Internal;

/// <summary>
/// Internal carrier for a user-supplied MatchBy expression. Wrapped so future
/// fields (e.g. null-handling policy, case sensitivity, custom equality) can be
/// added without changing the public surface of <see cref="UpsertOptions"/>.
/// </summary>
internal sealed class MatchByConfiguration
{
    internal LambdaExpression Expression { get; }

    internal MatchByConfiguration(LambdaExpression expression)
    {
        Expression = expression ?? throw new ArgumentNullException(nameof(expression));
    }
}
