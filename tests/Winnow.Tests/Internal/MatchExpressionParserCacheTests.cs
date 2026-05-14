using System.Reflection;
using Shouldly;
using Winnow.Internal;

namespace Winnow.Tests.Internal;

/// <summary>
/// MatchExpressionParser caches parsed plans by expression instance. The cache key
/// must include <see cref="Type"/> for <c>TEntity</c> — otherwise a misuse like
/// <c>Parse&lt;EntityA&gt;(expr, entityTypeForB)</c> with the same (expression, entityType)
/// pair could return a plan typed for the wrong entity and NRE on the cast.
/// </summary>
public class MatchExpressionParserCacheTests
{
    [Fact]
    public void PlanCache_KeyIncludesTypeForTEntity()
    {
        var cacheField = typeof(MatchExpressionParser)
            .GetField("PlanCache", BindingFlags.Static | BindingFlags.NonPublic);
        cacheField.ShouldNotBeNull("PlanCache field must exist for this test to verify its shape.");

        var cache = cacheField!.GetValue(null);
        cache.ShouldNotBeNull();

        var dictType = cache!.GetType();
        var keyType = dictType.GenericTypeArguments[0];

        // The key is a tuple. Its generic arguments must include System.Type so that
        // the cache discriminates plans by the requested TEntity, not just by the
        // (expression, entityType) pair.
        keyType.GenericTypeArguments.ShouldContain(typeof(Type),
            $"PlanCache key {keyType} must include typeof(TEntity) to prevent cross-type plan collisions. " +
            $"Current key tuple parts: {string.Join(", ", keyType.GenericTypeArguments.Select(t => t.Name))}.");
    }
}
