using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests;

public class BatchSaverDeleteGraphTests : TestBase
{
    [Fact]
    public void DeleteGraphBatch_CascadeDeletesChildrenFirst()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, 2);

        var orderWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First();
        var orderId = orderWithChildren.Id;
        var childIds = orderWithChildren.OrderItems.Select(i => i.Id).ToList();
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.DeleteGraphBatch([orderWithChildren]);

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
    public void DeleteGraphBatch_ReturnsChildIdsByParentId()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, 2);

        var orderWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First();
        var orderId = orderWithChildren.Id;
        var expectedChildIds = orderWithChildren.OrderItems.Select(i => i.Id).ToList();
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.DeleteGraphBatch([orderWithChildren]);

        result.ChildIdsByParentId.ShouldNotBeNull();
        result.ChildIdsByParentId.ShouldContainKey(orderId);
        result.ChildIdsByParentId![orderId].ShouldBe(expectedChildIds);
    }

    [Fact]
    public void DeleteGraphBatch_ThrowBehavior_ThrowsIfChildrenExist()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, 2);

        var orderWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First();
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var options = new DeleteGraphBatchOptions { CascadeBehavior = DeleteCascadeBehavior.Throw };

        Should.Throw<InvalidOperationException>(() => saver.DeleteGraphBatch([orderWithChildren], options))
            .Message.ShouldContain("child(ren)");
    }

    [Fact]
    public void DeleteGraphBatch_ThrowBehavior_AllowsNoChildren()
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

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var options = new DeleteGraphBatchOptions { CascadeBehavior = DeleteCascadeBehavior.Throw };

        var result = saver.DeleteGraphBatch([emptyOrder], options);

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public void DeleteGraphBatch_ParentOnlyBehavior_OnlyDeletesParent()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, 2);

        var orderWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First();
        var orderId = orderWithChildren.Id;
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var options = new DeleteGraphBatchOptions { CascadeBehavior = DeleteCascadeBehavior.ParentOnly };

        var result = saver.DeleteGraphBatch([orderWithChildren], options);

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.CustomerOrders.Find(orderId).ShouldBeNull();
    }

    [Fact]
    public void DeleteGraphBatch_MultipleGraphs_EachIsolated()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 5, 2);

        var ordersWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .Take(3)
            .ToList();
        var deletedOrderIds = ordersWithChildren.Select(o => o.Id).ToList();
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.DeleteGraphBatch(ordersWithChildren);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.ChildIdsByParentId.ShouldNotBeNull();
        result.ChildIdsByParentId!.Count.ShouldBe(3);

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(2);
        foreach (var orderId in deletedOrderIds)
        {
            context.CustomerOrders.Find(orderId).ShouldBeNull();
        }
    }

    [Fact]
    public void DeleteGraphBatch_OneByOne_CorrectRoundTrips()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, 2);

        var ordersWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ToList();
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var options = new DeleteGraphBatchOptions { Strategy = BatchStrategy.OneByOne };
        var result = saver.DeleteGraphBatch(ordersWithChildren, options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.DatabaseRoundTrips.ShouldBe(3);
    }

    [Fact]
    public void DeleteGraphBatch_DivideAndConquer_EfficientOnSuccess()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 8, 2);

        var ordersWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ToList();
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var options = new DeleteGraphBatchOptions { Strategy = BatchStrategy.DivideAndConquer };
        var result = saver.DeleteGraphBatch(ordersWithChildren, options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(8);
        result.DatabaseRoundTrips.ShouldBeLessThan(8);
    }

    [Fact]
    public void DeleteGraphBatch_EmptyChildren_ParentStillDeletes()
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

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.DeleteGraphBatch([orderToDelete]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.ChildIdsByParentId.ShouldNotBeNull();
        result.ChildIdsByParentId![orderId].ShouldBeEmpty();

        context.ChangeTracker.Clear();
        context.CustomerOrders.Find(orderId).ShouldBeNull();
    }

    [Fact]
    public void DeleteGraphBatch_LargeBatch_PerformanceTest()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 50, 2);

        var ordersWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ToList();
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.DeleteGraphBatch(ordersWithChildren);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(50);

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(0);
        context.OrderItems.Count().ShouldBe(0);
    }
}
