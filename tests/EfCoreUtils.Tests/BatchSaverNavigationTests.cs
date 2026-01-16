using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests;

public class BatchSaverNavigationTests : TestBase
{
    [Fact]
    public void UpdateBatch_WhenChildrenModified_ThrowsHelpfulException()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 10, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        orders[0].OrderItems.First().Quantity = 999;

        // Ensure EF Core detects the change
        context.ChangeTracker.DetectChanges();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var ex = Should.Throw<InvalidOperationException>(() => saver.UpdateBatch(orders));

        ex.Message.ShouldContain("navigation properties");
        ex.Message.ShouldContain("OrderItems");
    }

    [Fact]
    public void UpdateBatch_WithValidationDisabled_IgnoresModifiedChildren()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 10, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var originalQuantity = orders[0].OrderItems.First().Quantity;
        orders[0].OrderItems.First().Quantity = 999;
        orders[0].Status = CustomerOrderStatus.Processing;

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateBatch(orders, new BatchOptions
        {
            ValidateNavigationProperties = false
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        // Verify child not updated in database
        context.ChangeTracker.Clear();
        var verify = context.OrderItems.Find(orders[0].OrderItems.First().Id);
        verify.ShouldNotBeNull();
        verify.Quantity.ShouldBe(originalQuantity);
    }

    [Fact]
    public void UpdateBatch_WithLoadedChildren_HandlesValidationFailuresCorrectly()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 10, itemsPerOrder: 3);

        // Load orders with navigation properties
        var orders = context.CustomerOrders.Include(o => o.OrderItems).Take(10).ToList();

        // Make first order invalid (negative total)
        orders[0].TotalAmount = -100m;

        // Make remaining orders valid updates
        for (int i = 1; i < orders.Count; i++)
        {
            orders[i].TotalAmount += 10m;
        }

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateBatch(orders, new BatchOptions
        {
            ValidateNavigationProperties = false
        });

        // Should have partial success: 9 valid, 1 invalid
        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(9);
        result.FailureCount.ShouldBe(1);
        result.Failures[0].Reason.ShouldBe(FailureReason.ValidationError);
        result.Failures[0].EntityId.ShouldBe(orders[0].Id);
    }

    [Fact]
    public void UpdateBatch_AfterCompletion_AllEntitiesDetached()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 10, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).Take(10).ToList();

        foreach (var order in orders)
        {
            order.Status = CustomerOrderStatus.Processing;
        }

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateBatch(orders, new BatchOptions
        {
            ValidateNavigationProperties = false
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        // Verify all parent AND child entities are detached
        foreach (var order in orders)
        {
            context.Entry(order).State.ShouldBe(EntityState.Detached);
            foreach (var item in order.OrderItems)
            {
                context.Entry(item).State.ShouldBe(EntityState.Detached);
            }
        }
    }

    [Fact]
    public void UpdateParentOnly_WithLoadedChildren_DatabaseVerification()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 10, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var originalChildQuantity = orders[0].OrderItems.First().Quantity;
        var originalTotalAmount = orders[0].TotalAmount;

        foreach (var order in orders)
        {
            order.TotalAmount += 100m;
        }

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateBatch(orders, new BatchOptions
        {
            ValidateNavigationProperties = false
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        // Clear and reload from database
        context.ChangeTracker.Clear();
        var verifyOrders = context.CustomerOrders.Include(o => o.OrderItems).ToList();

        // Verify parent was updated
        verifyOrders[0].TotalAmount.ShouldBe(originalTotalAmount + 100m);

        // Verify children were NOT updated
        verifyOrders[0].OrderItems.First().Quantity.ShouldBe(originalChildQuantity);
    }

    [Fact]
    public void UpdateParentOnly_WithLoadedChildren_UpdatesOnlyParent()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 10, itemsPerOrder: 3);

        var ordersToUpdate = context.CustomerOrders
            .Include(o => o.OrderItems)
            .Take(10)
            .ToList();

        foreach (var order in ordersToUpdate)
        {
            order.TotalAmount += 10.00m;
            order.Status = CustomerOrderStatus.Processing;
        }

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateBatch(ordersToUpdate, new BatchOptions
        {
            ValidateNavigationProperties = false
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(10);
        result.FailureCount.ShouldBe(0);

        // Verify children were NOT modified
        context.ChangeTracker.Clear();
        var verifyOrder = context.CustomerOrders.Include(o => o.OrderItems).First();
        verifyOrder.OrderItems.Count.ShouldBe(3);
    }

    [Fact]
    public void UpdateLargeBatch_WithEntityGraphs_CompletesSuccessfully()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 1000, itemsPerOrder: 5);

        var ordersToUpdate = context.CustomerOrders
            .Include(o => o.OrderItems)
            .Take(1000)
            .ToList();

        foreach (var order in ordersToUpdate)
        {
            order.Status = CustomerOrderStatus.Completed;
        }

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateBatch(ordersToUpdate, new BatchOptions
        {
            ValidateNavigationProperties = false
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1000);
        result.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void UpdateOrderItems_DirectlyUsingBatchSaver_WorksCorrectly()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 10, itemsPerOrder: 3);

        var itemsToUpdate = context.OrderItems.Take(20).ToList();
        foreach (var item in itemsToUpdate)
        {
            item.Quantity += 1;
            item.Subtotal = item.Quantity * item.UnitPrice;
        }

        var saver = new BatchSaver<OrderItem, int>(context);
        var result = saver.UpdateBatch(itemsToUpdate);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(20);
    }
}
