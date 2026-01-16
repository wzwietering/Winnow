using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests;

public class DivideAndConquerGraphStrategyTests : TestBase
{
    private static readonly GraphBatchOptions DivideAndConquerOptions = new()
    {
        Strategy = BatchStrategy.DivideAndConquer
    };

    [Fact]
    public void UpdateGraphBatch_DivideAndConquer_AllSucceed_SingleRoundTrip()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 5, itemsPerOrder: 3);

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
        var result = saver.UpdateGraphBatch(orders, DivideAndConquerOptions);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(5);
        result.DatabaseRoundTrips.ShouldBe(1);
    }

    [Fact]
    public void UpdateGraphBatch_DivideAndConquer_SingleEntity()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 1, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        orders[0].Status = CustomerOrderStatus.Processing;
        var firstItem = orders[0].OrderItems.First();
        firstItem.Quantity = 999;
        firstItem.Subtotal = firstItem.Quantity * firstItem.UnitPrice;

        context.ChangeTracker.DetectChanges();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, DivideAndConquerOptions);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);
        result.DatabaseRoundTrips.ShouldBe(1);

        context.ChangeTracker.Clear();
        var verifyItem = context.OrderItems.Find(orders[0].OrderItems.First().Id);
        verifyItem!.Quantity.ShouldBe(999);
    }

    [Fact]
    public void UpdateGraphBatch_DivideAndConquer_EmptyList()
    {
        using var context = CreateContext();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch([], DivideAndConquerOptions);

        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
        result.GraphHierarchy.ShouldNotBeNull();
        result.GraphHierarchy!.Count.ShouldBe(0);
    }

    [Fact]
    public void UpdateGraphBatch_DivideAndConquer_ReturnsGraphHierarchy()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();

        foreach (var order in orders)
        {
            order.Status = CustomerOrderStatus.Completed;
        }

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, DivideAndConquerOptions);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.GraphHierarchy.ShouldNotBeNull();
        result.GraphHierarchy!.Count.ShouldBe(3);

        foreach (var order in orders)
        {
            result.GraphHierarchy!.ShouldContain(n => n.EntityId.Equals(order.Id));
            result.GraphHierarchy!.First(n => n.EntityId.Equals(order.Id)).GetChildIds().Count.ShouldBe(3);
        }
    }

    [Fact]
    public void UpdateGraphBatch_DivideAndConquer_OneFailsOthersSucceed()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 5, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();

        orders[0].TotalAmount = -100m;
        orders[1].Status = CustomerOrderStatus.Processing;
        orders[2].Status = CustomerOrderStatus.Completed;
        orders[3].Status = CustomerOrderStatus.Cancelled;
        orders[4].Status = CustomerOrderStatus.Processing;

        context.ChangeTracker.DetectChanges();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, DivideAndConquerOptions);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
        result.FailureCount.ShouldBe(1);
        result.Failures[0].EntityId.ShouldBe(orders[0].Id);

        context.ChangeTracker.Clear();
        context.CustomerOrders.Find(orders[1].Id)!.Status.ShouldBe(CustomerOrderStatus.Processing);
        context.CustomerOrders.Find(orders[2].Id)!.Status.ShouldBe(CustomerOrderStatus.Completed);
    }

    [Fact]
    public void UpdateGraphBatch_DivideAndConquer_MultipleFailures_ScatteredPositions()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 8, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();

        orders[1].TotalAmount = -100m;
        orders[4].TotalAmount = -200m;
        orders[6].TotalAmount = -300m;

        context.ChangeTracker.DetectChanges();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, DivideAndConquerOptions);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(5);
        result.FailureCount.ShouldBe(3);

        var failedIds = result.Failures.Select(f => f.EntityId).ToHashSet();
        failedIds.ShouldContain(orders[1].Id);
        failedIds.ShouldContain(orders[4].Id);
        failedIds.ShouldContain(orders[6].Id);
    }

    [Fact]
    public void UpdateGraphBatch_DivideAndConquer_ParentFails_ChildNotSaved()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var originalChildQuantity = orders[0].OrderItems.First().Quantity;

        orders[0].TotalAmount = -100m;
        orders[0].OrderItems.First().Quantity = 999;

        context.ChangeTracker.DetectChanges();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, DivideAndConquerOptions);

        result.IsPartialSuccess.ShouldBeTrue();
        result.Failures.Count.ShouldBe(1);
        result.Failures[0].EntityId.ShouldBe(orders[0].Id);

        context.ChangeTracker.Clear();
        var verifyItem = context.OrderItems.Find(orders[0].OrderItems.First().Id);
        verifyItem!.Quantity.ShouldBe(originalChildQuantity);
    }

    [Fact]
    public void UpdateGraphBatch_DivideAndConquer_ChildFails_ParentNotSaved()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var originalStatus = orders[0].Status;

        orders[0].Status = CustomerOrderStatus.Completed;
        orders[0].OrderItems.First().Quantity = -1;

        context.ChangeTracker.DetectChanges();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, DivideAndConquerOptions);

        result.IsPartialSuccess.ShouldBeTrue();
        result.FailureCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        var verifyOrder = context.CustomerOrders.Find(orders[0].Id);
        verifyOrder!.Status.ShouldBe(originalStatus);
    }

    [Fact]
    public void UpdateGraphBatch_DivideAndConquer_AllFail()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 4, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();

        foreach (var order in orders)
        {
            order.TotalAmount = -100m;
        }

        context.ChangeTracker.DetectChanges();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, DivideAndConquerOptions);

        result.IsCompleteFailure.ShouldBeTrue();
        result.FailureCount.ShouldBe(4);
        result.SuccessCount.ShouldBe(0);
    }

    // ========== Orphan Handling Tests ==========

    [Fact]
    public void UpdateGraphBatch_DivideAndConquer_OrphanThrow_ThrowsUpfront()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var removedItemId = orders[0].OrderItems.First().Id;

        orders[0].OrderItems.Remove(orders[0].OrderItems.First());

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var ex = Should.Throw<InvalidOperationException>(() =>
            saver.UpdateGraphBatch(orders, DivideAndConquerOptions));

        ex.Message.ShouldContain("orphaned");
        ex.Message.ShouldContain(removedItemId.ToString());
    }

    [Fact]
    public void UpdateGraphBatch_DivideAndConquer_OrphanDelete_BatchSucceeds()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var removedItemId = orders[0].OrderItems.First().Id;

        orders[0].OrderItems.Remove(orders[0].OrderItems.First());
        orders[0].Status = CustomerOrderStatus.Processing;

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
        {
            Strategy = BatchStrategy.DivideAndConquer,
            OrphanedChildBehavior = OrphanBehavior.Delete
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.OrderItems.Find(removedItemId).ShouldBeNull();

        var verifyOrder = context.CustomerOrders.Include(o => o.OrderItems).First(o => o.Id == orders[0].Id);
        verifyOrder.OrderItems.Count.ShouldBe(2);
    }

    [Fact]
    public void UpdateGraphBatch_DivideAndConquer_OrphanDelete_AfterSplit()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 4, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var removedItemId = orders[0].OrderItems.First().Id;

        orders[0].OrderItems.Remove(orders[0].OrderItems.First());
        orders[0].Status = CustomerOrderStatus.Processing;
        orders[1].TotalAmount = -100m;

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
        {
            Strategy = BatchStrategy.DivideAndConquer,
            OrphanedChildBehavior = OrphanBehavior.Delete
        });

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.FailureCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        context.OrderItems.Find(removedItemId).ShouldBeNull();
    }

    [Fact]
    public void UpdateGraphBatch_DivideAndConquer_OrphanDelete_MultipleOrphansMultipleGraphs()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var removedItem1 = orders[0].OrderItems.First();
        var removedItem2 = orders[1].OrderItems.First();

        orders[0].OrderItems.Remove(removedItem1);
        orders[1].OrderItems.Remove(removedItem2);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
        {
            Strategy = BatchStrategy.DivideAndConquer,
            OrphanedChildBehavior = OrphanBehavior.Delete
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.OrderItems.Find(removedItem1.Id).ShouldBeNull();
        context.OrderItems.Find(removedItem2.Id).ShouldBeNull();

        var verifyOrder0 = context.CustomerOrders.Include(o => o.OrderItems).First(o => o.Id == orders[0].Id);
        var verifyOrder1 = context.CustomerOrders.Include(o => o.OrderItems).First(o => o.Id == orders[1].Id);
        verifyOrder0.OrderItems.Count.ShouldBe(2);
        verifyOrder1.OrderItems.Count.ShouldBe(2);
    }

    [Fact]
    public void UpdateGraphBatch_DivideAndConquer_OrphanDetach()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 3);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        var removedItemId = orders[0].OrderItems.First().Id;

        orders[0].OrderItems.Remove(orders[0].OrderItems.First());
        orders[0].Status = CustomerOrderStatus.Processing;

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
        {
            Strategy = BatchStrategy.DivideAndConquer,
            OrphanedChildBehavior = OrphanBehavior.Detach
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var orphanedItem = context.OrderItems.Find(removedItemId);
        orphanedItem.ShouldNotBeNull();
    }

    // ========== Large Batch Test ==========

    [Fact]
    public void UpdateGraphBatch_DivideAndConquer_LargeBatch()
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
        var result = saver.UpdateGraphBatch(orders, DivideAndConquerOptions);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(100);
        result.DatabaseRoundTrips.ShouldBe(1);
    }
}
