using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

public class WinnowerDeleteGraphTests : TestBase
{
    [Fact]
    public void DeleteGraph_CascadeDeletesChildrenFirst()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, 2);

        var orderWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First();
        var orderId = orderWithChildren.Id;
        var childIds = orderWithChildren.OrderItems.Select(i => i.Id).ToList();
        context.ChangeTracker.Clear();

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.DeleteGraph([orderWithChildren]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);
        result.SuccessfulIds.ShouldContain(orderId);

        context.ChangeTracker.Clear();
        context.CustomerOrders.Find(orderId).ShouldBeNull();
        foreach (var childId in childIds)
        {
            context.OrderItems.Find(childId).ShouldBeNull();
        }
    }

    [Fact]
    public void DeleteGraph_ReturnsGraphHierarchy()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, 2);

        var orderWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First();
        var orderId = orderWithChildren.Id;
        var expectedChildIds = orderWithChildren.OrderItems.Select(i => i.Id).ToList();
        context.ChangeTracker.Clear();

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.DeleteGraph([orderWithChildren]);

        result.GraphHierarchy.ShouldNotBeNull();
        result.GraphHierarchy!.ShouldContain(n => n.EntityId.Equals(orderId));
        result.GraphHierarchy!.First(n => n.EntityId.Equals(orderId)).GetChildIds().ShouldBe(expectedChildIds);
    }

    [Fact]
    public void DeleteGraph_ThrowBehavior_ThrowsIfChildrenExist()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, 2);

        var orderWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First();
        context.ChangeTracker.Clear();

        var saver = new Winnower<CustomerOrder, int>(context);
        var options = new DeleteGraphOptions { CascadeBehavior = DeleteCascadeBehavior.Throw };

        Should.Throw<InvalidOperationException>(() => saver.DeleteGraph([orderWithChildren], options))
            .Message.ShouldContain("child(ren)");
    }

    [Fact]
    public void DeleteGraph_ThrowBehavior_AllowsNoChildren()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, 2);

        var orderWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First();
        orderWithChildren.OrderItems.Clear();
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var emptyOrder = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First(o => o.Id == orderWithChildren.Id);
        context.ChangeTracker.Clear();

        var saver = new Winnower<CustomerOrder, int>(context);
        var options = new DeleteGraphOptions { CascadeBehavior = DeleteCascadeBehavior.Throw };

        var result = saver.DeleteGraph([emptyOrder], options);

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public void DeleteGraph_ParentOnlyBehavior_OnlyDeletesParent()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, 2);

        var orderWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First();
        var orderId = orderWithChildren.Id;
        context.ChangeTracker.Clear();

        var saver = new Winnower<CustomerOrder, int>(context);
        var options = new DeleteGraphOptions { CascadeBehavior = DeleteCascadeBehavior.ParentOnly };

        var result = saver.DeleteGraph([orderWithChildren], options);

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.CustomerOrders.Find(orderId).ShouldBeNull();
    }

    [Fact]
    public void DeleteGraph_MultipleGraphs_EachIsolated()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 5, 2);

        var ordersWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .Take(3)
            .ToList();
        var deletedOrderIds = ordersWithChildren.Select(o => o.Id).ToList();
        context.ChangeTracker.Clear();

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.DeleteGraph(ordersWithChildren);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.GraphHierarchy.ShouldNotBeNull();
        result.GraphHierarchy!.Count.ShouldBe(3);

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(2);
        foreach (var orderId in deletedOrderIds)
        {
            context.CustomerOrders.Find(orderId).ShouldBeNull();
        }
    }

    [Fact]
    public void DeleteGraph_OneByOne_CorrectRoundTrips()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, 2);

        var ordersWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ToList();
        context.ChangeTracker.Clear();

        var saver = new Winnower<CustomerOrder, int>(context);
        var options = new DeleteGraphOptions { Strategy = BatchStrategy.OneByOne };
        var result = saver.DeleteGraph(ordersWithChildren, options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.DatabaseRoundTrips.ShouldBe(3);
    }

    [Fact]
    public void DeleteGraph_DivideAndConquer_EfficientOnSuccess()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 8, 2);

        var ordersWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ToList();
        context.ChangeTracker.Clear();

        var saver = new Winnower<CustomerOrder, int>(context);
        var options = new DeleteGraphOptions { Strategy = BatchStrategy.DivideAndConquer };
        var result = saver.DeleteGraph(ordersWithChildren, options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(8);
        result.DatabaseRoundTrips.ShouldBeLessThan(8);
    }

    [Fact]
    public void DeleteGraph_EmptyChildren_ParentStillDeletes()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-EMPTY",
            CustomerName = "Test Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 0m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = []
        };
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        var orderId = order.Id;
        context.ChangeTracker.Clear();

        var orderToDelete = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First(o => o.Id == orderId);
        context.ChangeTracker.Clear();

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.DeleteGraph([orderToDelete]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.GraphHierarchy.ShouldNotBeNull();
        result.GraphHierarchy!.First(n => n.EntityId.Equals(orderId)).GetChildIds().ShouldBeEmpty();

        context.ChangeTracker.Clear();
        context.CustomerOrders.Find(orderId).ShouldBeNull();
    }

    [Fact]
    public void DeleteGraph_LargeDataSet_AllSucceed()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 50, 2);

        var ordersWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ToList();
        context.ChangeTracker.Clear();

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.DeleteGraph(ordersWithChildren);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(50);

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(0);
        context.OrderItems.Count().ShouldBe(0);
    }
}
