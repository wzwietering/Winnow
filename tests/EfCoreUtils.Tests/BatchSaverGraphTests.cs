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

        var saver = new BatchSaver<CustomerOrder, int>(context);
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

        var saver = new BatchSaver<CustomerOrder, int>(context);
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

        var saver = new BatchSaver<CustomerOrder, int>(context);
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

        var saver = new BatchSaver<CustomerOrder, int>(context);
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

        var saver = new BatchSaver<CustomerOrder, int>(context);
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

        var saver = new BatchSaver<CustomerOrder, int>(context);
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

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch([]);

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

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(100);
        result.ChildIdsByParentId.ShouldNotBeNull();
        result.ChildIdsByParentId.Count.ShouldBe(100);
        result.DatabaseRoundTrips.ShouldBe(100); // OneByOne = 100 round trips
    }

    // ========== Orphan Detection Tests ==========

    [Fact]
    public void UpdateGraphBatch_OrphanThrow_ThrowsWhenChildRemoved()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var removedItemId = orders[0].OrderItems.First().Id;

        // Remove a child from the collection
        orders[0].OrderItems.Remove(orders[0].OrderItems.First());

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var ex = Should.Throw<InvalidOperationException>(() =>
            saver.UpdateGraphBatch(orders)); // Default is OrphanBehavior.Throw

        ex.Message.ShouldContain("orphaned");
        ex.Message.ShouldContain(removedItemId.ToString());
    }

    [Fact]
    public void UpdateGraphBatch_OrphanDelete_RemovesChildFromDatabase()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var removedItemId = orders[0].OrderItems.First().Id;

        // Remove a child from the collection
        orders[0].OrderItems.Remove(orders[0].OrderItems.First());
        orders[0].Status = CustomerOrderStatus.Processing;

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Delete
        });

        // Debug: check failures
        if (result.Failures.Any())
        {
            var failureMessages = string.Join("; ", result.Failures.Select(f => $"Id={f.EntityId}: {f.ErrorMessage} ({f.Reason})"));
            throw new Exception($"Unexpected failures: {failureMessages}");
        }

        result.IsCompleteSuccess.ShouldBeTrue();

        // Verify child was deleted from database
        context.ChangeTracker.Clear();
        var deletedItem = context.OrderItems.Find(removedItemId);
        deletedItem.ShouldBeNull();

        // Verify remaining children still exist
        var verifyOrder = context.CustomerOrders.Include(o => o.OrderItems).First(o => o.Id == orders[0].Id);
        verifyOrder.OrderItems.Count.ShouldBe(2);
    }

    [Fact]
    public void UpdateGraphBatch_OrphanDetach_LeavesChildInDatabase()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var removedItemId = orders[0].OrderItems.First().Id;

        // Remove a child from the collection
        orders[0].OrderItems.Remove(orders[0].OrderItems.First());
        orders[0].Status = CustomerOrderStatus.Processing;

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Detach
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        // Verify child still exists in database (orphaned but not deleted)
        context.ChangeTracker.Clear();
        var orphanedItem = context.OrderItems.Find(removedItemId);
        orphanedItem.ShouldNotBeNull();
    }

    [Fact]
    public void UpdateGraphBatch_OrphanDelete_AllChildrenRemoved()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var removedItemIds = orders[0].OrderItems.Select(i => i.Id).ToList();

        // Remove ALL children from the collection
        orders[0].OrderItems.Clear();
        orders[0].Status = CustomerOrderStatus.Completed;

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Delete
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        // Verify all children were deleted from database
        context.ChangeTracker.Clear();
        foreach (var itemId in removedItemIds)
        {
            var deletedItem = context.OrderItems.Find(itemId);
            deletedItem.ShouldBeNull();
        }

        // Verify order still exists
        var verifyOrder = context.CustomerOrders.Include(o => o.OrderItems).First(o => o.Id == orders[0].Id);
        verifyOrder.OrderItems.Count.ShouldBe(0);
    }

    [Fact]
    public void UpdateGraphBatch_OrphanDelete_MultipleOrphansFromMultipleGraphs()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var removedItem1 = orders[0].OrderItems.First();
        var removedItem2 = orders[1].OrderItems.First();

        // Remove one child from each of two orders
        orders[0].OrderItems.Remove(removedItem1);
        orders[1].OrderItems.Remove(removedItem2);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Delete
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        // Verify both orphans were deleted
        context.ChangeTracker.Clear();
        context.OrderItems.Find(removedItem1.Id).ShouldBeNull();
        context.OrderItems.Find(removedItem2.Id).ShouldBeNull();

        // Verify remaining children
        var verifyOrder0 = context.CustomerOrders.Include(o => o.OrderItems).First(o => o.Id == orders[0].Id);
        var verifyOrder1 = context.CustomerOrders.Include(o => o.OrderItems).First(o => o.Id == orders[1].Id);
        verifyOrder0.OrderItems.Count.ShouldBe(2);
        verifyOrder1.OrderItems.Count.ShouldBe(2);
    }
}
