using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Internal;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests.Internal;

/// <summary>
/// MatchExpressionParser caches parsed plans by expression instance. The cache key
/// must include <see cref="Type"/> for <c>TEntity</c> — otherwise a misuse like
/// <c>Parse&lt;EntityA&gt;(expr, entityTypeForB)</c> with the same (expression, entityType)
/// pair could return a plan typed for the wrong entity and NRE on the cast.
/// </summary>
public class MatchExpressionParserCacheTests : TestBase
{
    [Fact]
    public void Parse_DifferentTEntityTypes_ProduceSeparatePlans_ThatExtractFromTheirOwnType()
    {
        // Behavioral companion to the structural PlanCache_KeyIncludesTypeForTEntity test:
        // pin that two entity types using a same-named property produce independent plans
        // whose extractors actually pull from the correct entity instance type.
        using var context = CreateContext();
        var productEntityType = context.Model.FindEntityType(typeof(Product))!;
        var orderEntityType = context.Model.FindEntityType(typeof(CustomerOrder))!;

        // Both Product and CustomerOrder have a 'CustomerName' / 'Name' property — we use
        // the genuinely-distinct member names to keep the test independent of unrelated
        // schema changes. The point is that two TEntity types compile to different plans.
        Expression<Func<Product, object>> productExpr = p => p.Name;
        Expression<Func<CustomerOrder, object>> orderExpr = o => o.OrderNumber;

        var productPlan = MatchExpressionParser.Parse<Product>(productExpr, productEntityType);
        var orderPlan = MatchExpressionParser.Parse<CustomerOrder>(orderExpr, orderEntityType);

        productPlan.ShouldNotBeSameAs(orderPlan);

        var product = new Product { Name = "Hello", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow };
        var order = new CustomerOrder { OrderNumber = "ORD-1", CustomerName = "X", TotalAmount = 1m };

        productPlan.Extractor(product)[0].ShouldBe("Hello");
        orderPlan.Extractor(order)[0].ShouldBe("ORD-1");
    }

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
