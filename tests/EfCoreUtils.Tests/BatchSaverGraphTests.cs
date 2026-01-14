using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests;

public class BatchSaverGraphTests : TestBase
{
    [Fact]
    public void UpdateGraphBatch_ParentAndChildModified_BothUpdated()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 5, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();

        // Modify both parent and child
        orders[0].Status = CustomerOrderStatus.Processing;
        var firstItem = orders[0].OrderItems.First();
        firstItem.Quantity = 999;
        firstItem.Subtotal = firstItem.Quantity * firstItem.UnitPrice;

        context.ChangeTracker.DetectChanges();

        var saver = new BatchSaver<CustomerOrder>(context);
        var result = saver.UpdateGraphBatch(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(5);

        // Verify database state
        context.ChangeTracker.Clear();
        var verifyOrder = context.CustomerOrders.Include(o => o.OrderItems).First(o => o.Id == orders[0].Id);
        verifyOrder.Status.ShouldBe(CustomerOrderStatus.Processing);
        verifyOrder.OrderItems.First().Quantity.ShouldBe(999);
    }

    [Fact]
    public void UpdateGraphBatch_ParentFails_EntireGraphRollsBack()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 5, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var originalChildQuantity = orders[0].OrderItems.First().Quantity;

        // Make parent invalid but child valid
        orders[0].TotalAmount = -100m; // Invalid
        var firstItem = orders[0].OrderItems.First();
        firstItem.Quantity = 999;
        firstItem.Subtotal = firstItem.Quantity * firstItem.UnitPrice; // Valid change

        context.ChangeTracker.DetectChanges();

        var saver = new BatchSaver<CustomerOrder>(context);
        var result = saver.UpdateGraphBatch(orders);

        // First graph should fail, others succeed
        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
        result.FailureCount.ShouldBe(1);
        result.Failures[0].EntityId.ShouldBe(orders[0].Id);

        // Verify child was NOT updated (rolled back with parent)
        context.ChangeTracker.Clear();
        var verifyItem = context.OrderItems.Find(orders[0].OrderItems.First().Id);
        verifyItem.ShouldNotBeNull();
        verifyItem.Quantity.ShouldBe(originalChildQuantity);
    }

    [Fact]
    public void UpdateGraphBatch_ChildFails_EntireGraphRollsBack()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 5, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var originalStatus = orders[0].Status;

        // Make parent valid but child invalid
        orders[0].Status = CustomerOrderStatus.Completed;
        orders[0].OrderItems.First().Quantity = -1; // Invalid

        context.ChangeTracker.DetectChanges();

        var saver = new BatchSaver<CustomerOrder>(context);
        var result = saver.UpdateGraphBatch(orders);

        // First graph should fail due to child, others succeed
        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
        result.FailureCount.ShouldBe(1);

        // Verify parent was NOT updated (rolled back with child)
        context.ChangeTracker.Clear();
        var verifyOrder = context.CustomerOrders.Find(orders[0].Id);
        verifyOrder.ShouldNotBeNull();
        verifyOrder.Status.ShouldBe(originalStatus);
    }

    [Fact]
    public void UpdateGraphBatch_ReturnsChildIdsByParentId()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();

        foreach (var order in orders)
        {
            order.Status = CustomerOrderStatus.Completed;
        }

        var saver = new BatchSaver<CustomerOrder>(context);
        var result = saver.UpdateGraphBatch(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.ChildIdsByParentId.ShouldNotBeNull();
        result.ChildIdsByParentId.Count.ShouldBe(3);

        foreach (var order in orders)
        {
            result.ChildIdsByParentId.ShouldContainKey(order.Id);
            result.ChildIdsByParentId[order.Id].Count.ShouldBe(3);
        }
    }

    [Fact]
    public void UpdateGraphBatch_MultipleGraphs_OneFailsOthersSucceed()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 5, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();

        // Make orders 0 and 2 invalid
        orders[0].TotalAmount = -100m;
        orders[2].TotalAmount = -200m;

        // Make orders 1, 3, 4 valid updates
        orders[1].Status = CustomerOrderStatus.Processing;
        orders[3].Status = CustomerOrderStatus.Completed;
        orders[4].Status = CustomerOrderStatus.Cancelled;

        context.ChangeTracker.DetectChanges();

        var saver = new BatchSaver<CustomerOrder>(context);
        var result = saver.UpdateGraphBatch(orders);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.FailureCount.ShouldBe(2);

        // Verify successful updates in database
        context.ChangeTracker.Clear();
        context.CustomerOrders.Find(orders[1].Id)!.Status.ShouldBe(CustomerOrderStatus.Processing);
        context.CustomerOrders.Find(orders[3].Id)!.Status.ShouldBe(CustomerOrderStatus.Completed);
        context.CustomerOrders.Find(orders[4].Id)!.Status.ShouldBe(CustomerOrderStatus.Cancelled);
    }

    [Fact]
    public void UpdateGraphBatch_ChildOnlyModified_ParentUnchangedChildUpdated()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var originalStatus = orders[0].Status;

        // Only modify child, not parent
        var firstItem = orders[0].OrderItems.First();
        firstItem.Quantity = 999;
        firstItem.Subtotal = firstItem.Quantity * firstItem.UnitPrice;

        context.ChangeTracker.DetectChanges();

        var saver = new BatchSaver<CustomerOrder>(context);
        var result = saver.UpdateGraphBatch(orders);

        result.IsCompleteSuccess.ShouldBeTrue();

        // Verify child was updated
        context.ChangeTracker.Clear();
        var verifyItem = context.OrderItems.Find(orders[0].OrderItems.First().Id);
        verifyItem.ShouldNotBeNull();
        verifyItem.Quantity.ShouldBe(999);
    }

    [Fact]
    public void UpdateGraphBatch_EmptyCollection_ReturnsEmptyResult()
    {
        using var context = CreateContext();

        var saver = new BatchSaver<CustomerOrder>(context);
        var result = saver.UpdateGraphBatch(new List<CustomerOrder>());

        result.IsCompleteSuccess.ShouldBeFalse();
        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
        result.ChildIdsByParentId.ShouldNotBeNull();
        result.ChildIdsByParentId.Count.ShouldBe(0);
    }

    [Fact]
    public void UpdateGraphBatch_LargeBatch_CompletesSuccessfully()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 100, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();

        foreach (var order in orders)
        {
            order.Status = CustomerOrderStatus.Completed;
            foreach (var item in order.OrderItems)
            {
                item.Quantity += 1;
                item.Subtotal = item.Quantity * item.UnitPrice;
            }
        }

        context.ChangeTracker.DetectChanges();

        var saver = new BatchSaver<CustomerOrder>(context);
        var result = saver.UpdateGraphBatch(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(100);
        result.ChildIdsByParentId.ShouldNotBeNull();
        result.ChildIdsByParentId.Count.ShouldBe(100);
        result.DatabaseRoundTrips.ShouldBe(100); // OneByOne = 100 round trips
    }
}
