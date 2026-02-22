using Winnow.Tests.Entities;
using Shouldly;

namespace Winnow.Tests;

public class NavigationFilterTests
{
    // ========== Include Mode Tests ==========

    [Fact]
    public void Include_ListedNavigation_ShouldTraverse()
    {
        NavigationFilter filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        filter.ShouldTraverse(typeof(CustomerOrder), "OrderItems").ShouldBeTrue();
    }

    [Fact]
    public void Include_UnlistedNavigation_ShouldNotTraverse()
    {
        NavigationFilter filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        filter.ShouldTraverse(typeof(OrderItem), "Reservations").ShouldBeFalse();
    }

    [Fact]
    public void Include_StrictMode_TypeWithoutRules_BlocksAllNavigations()
    {
        NavigationFilter filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        // OrderItem has no rules in include mode → NO navigations pass
        filter.ShouldTraverse(typeof(OrderItem), "Reservations").ShouldBeFalse();
        filter.ShouldTraverse(typeof(OrderItem), "CustomerOrder").ShouldBeFalse();
    }

    [Fact]
    public void Include_MultipleNavigationsOnSameType()
    {
        NavigationFilter filter = NavigationFilter.Include()
            .Navigation<OrderItem>(i => i.Reservations)
            .Navigation<OrderItem>(i => i.CustomerOrder);

        filter.ShouldTraverse(typeof(OrderItem), "Reservations").ShouldBeTrue();
        filter.ShouldTraverse(typeof(OrderItem), "CustomerOrder").ShouldBeTrue();
        filter.ShouldTraverse(typeof(OrderItem), "Product").ShouldBeFalse();
    }

    [Fact]
    public void Include_MultipleTypes()
    {
        NavigationFilter filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems)
            .Navigation<OrderItem>(i => i.Reservations);

        filter.ShouldTraverse(typeof(CustomerOrder), "OrderItems").ShouldBeTrue();
        filter.ShouldTraverse(typeof(OrderItem), "Reservations").ShouldBeTrue();
    }

    // ========== Exclude Mode Tests ==========

    [Fact]
    public void Exclude_ListedNavigation_ShouldNotTraverse()
    {
        NavigationFilter filter = NavigationFilter.Exclude()
            .Navigation<OrderItem>(i => i.Reservations);

        filter.ShouldTraverse(typeof(OrderItem), "Reservations").ShouldBeFalse();
    }

    [Fact]
    public void Exclude_UnlistedNavigation_ShouldTraverse()
    {
        NavigationFilter filter = NavigationFilter.Exclude()
            .Navigation<OrderItem>(i => i.Reservations);

        filter.ShouldTraverse(typeof(OrderItem), "CustomerOrder").ShouldBeTrue();
    }

    [Fact]
    public void Exclude_TypeWithoutRules_AllNavigationsPass()
    {
        NavigationFilter filter = NavigationFilter.Exclude()
            .Navigation<OrderItem>(i => i.Reservations);

        // CustomerOrder has no exclusion rules → all navigations pass
        filter.ShouldTraverse(typeof(CustomerOrder), "OrderItems").ShouldBeTrue();
    }

    // ========== Builder Tests ==========

    [Fact]
    public void Build_NoRules_Throws()
    {
        var builder = NavigationFilter.Include();

        Should.Throw<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Build_DuplicateNavigation_IsIdempotent()
    {
        NavigationFilter filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems)
            .Navigation<CustomerOrder>(o => o.OrderItems);

        filter.Rules.Count.ShouldBe(1);
        filter.Rules[typeof(CustomerOrder)].Count.ShouldBe(1);
    }

    [Fact]
    public void ImplicitConversion_BuilderToFilter()
    {
        NavigationFilter filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        filter.ShouldNotBeNull();
        filter.IsIncludeMode.ShouldBeTrue();
    }

    [Fact]
    public void IsIncludeMode_IncludeBuilder_True()
    {
        NavigationFilter filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        filter.IsIncludeMode.ShouldBeTrue();
    }

    [Fact]
    public void IsIncludeMode_ExcludeBuilder_False()
    {
        NavigationFilter filter = NavigationFilter.Exclude()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        filter.IsIncludeMode.ShouldBeFalse();
    }

    // ========== Expression Tests ==========

    [Fact]
    public void Expression_SimpleProperty_Extracted()
    {
        NavigationFilter filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        filter.ShouldTraverse(typeof(CustomerOrder), "OrderItems").ShouldBeTrue();
    }

    [Fact]
    public void Expression_MethodCall_Throws()
    {
        var builder = NavigationFilter.Include();

        Should.Throw<ArgumentException>(() =>
            builder.Navigation<CustomerOrder>(o => o.ToString()));
    }

    // ========== Navigations Plural Overload Tests ==========

    [Fact]
    public void Navigations_MultipleExpressions_AllRegistered()
    {
        NavigationFilter filter = NavigationFilter.Include()
            .Navigations<OrderItem>(i => i.Reservations, i => i.CustomerOrder);

        filter.ShouldTraverse(typeof(OrderItem), "Reservations").ShouldBeTrue();
        filter.ShouldTraverse(typeof(OrderItem), "CustomerOrder").ShouldBeTrue();
        filter.ShouldTraverse(typeof(OrderItem), "Product").ShouldBeFalse();
    }

    [Fact]
    public void Navigations_SingleExpression_Works()
    {
        NavigationFilter filter = NavigationFilter.Include()
            .Navigations<CustomerOrder>(o => o.OrderItems);

        filter.ShouldTraverse(typeof(CustomerOrder), "OrderItems").ShouldBeTrue();
    }

    [Fact]
    public void Navigations_CombinedWithNavigation_Works()
    {
        NavigationFilter filter = NavigationFilter.Include()
            .Navigations<CustomerOrder>(o => o.OrderItems)
            .Navigation<OrderItem>(i => i.Reservations);

        filter.ShouldTraverse(typeof(CustomerOrder), "OrderItems").ShouldBeTrue();
        filter.ShouldTraverse(typeof(OrderItem), "Reservations").ShouldBeTrue();
    }

    // ========== ToString Tests ==========

    [Fact]
    public void ToString_IncludeMode_ReadableOutput()
    {
        NavigationFilter filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var str = filter.ToString();
        str.ShouldContain("Include");
        str.ShouldContain("CustomerOrder");
        str.ShouldContain("OrderItems");
    }

    [Fact]
    public void ToString_ExcludeMode_ReadableOutput()
    {
        NavigationFilter filter = NavigationFilter.Exclude()
            .Navigation<OrderItem>(i => i.Reservations);

        var str = filter.ToString();
        str.ShouldContain("Exclude");
        str.ShouldContain("OrderItem");
        str.ShouldContain("Reservations");
    }
}
