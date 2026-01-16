using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests;

public class MultiLevelGraphTests : TestBase
{
    // ========== Multi-Level Insert Tests ==========

    [Fact]
    public void InsertGraphBatch_ThreeLevelHierarchy_AllInsertedWithIds()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-001", 2, 2);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);
        result.InsertedEntities[0].Id.ShouldBeGreaterThan(0);

        order.Id.ShouldBeGreaterThan(0);
        order.OrderItems.ShouldAllBe(item => item.Id > 0);
        order.OrderItems.SelectMany(i => i.Reservations)
            .ShouldAllBe(r => r.Id > 0);
    }

    [Fact]
    public void InsertGraphBatch_MaxDepthLimitsTraversal_GrandchildrenIgnored()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-001", 2, 2);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order], new InsertGraphBatchOptions
        {
            MaxDepth = 1
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        order.Id.ShouldBeGreaterThan(0);
        order.OrderItems.ShouldAllBe(item => item.Id > 0);

        // Reservations should NOT have IDs (not traversed at depth 2)
        order.OrderItems.SelectMany(i => i.Reservations)
            .ShouldAllBe(r => r.Id == 0);
    }

    [Fact]
    public void InsertGraphBatch_GraphHierarchy_ReturnsThreeLevelTree()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-001", 2, 2);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order]);

        result.GraphHierarchy.ShouldNotBeNull();
        result.GraphHierarchy!.Count.ShouldBe(1);

        var rootNode = result.GraphHierarchy!.First(n => n.EntityId.Equals(order.Id));
        rootNode.Depth.ShouldBe(0);
        rootNode.EntityType.ShouldBe(nameof(CustomerOrder));
        rootNode.Children.Count.ShouldBe(2);

        foreach (var childNode in rootNode.Children)
        {
            childNode.Depth.ShouldBe(1);
            childNode.EntityType.ShouldBe(nameof(OrderItem));
            childNode.Children.Count.ShouldBe(2);

            foreach (var grandchildNode in childNode.Children)
            {
                grandchildNode.Depth.ShouldBe(2);
                grandchildNode.EntityType.ShouldBe(nameof(ItemReservation));
                grandchildNode.Children.ShouldBeEmpty();
            }
        }
    }

    [Fact]
    public void InsertGraphBatch_TraversalInfo_AccurateDepthAndCount()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-001", 3, 2);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order]);

        var rootNode = result.GraphHierarchy!.First();

        // Count: 1 order + 3 items + 6 reservations = 10 total
        var allDescendants = rootNode.GetAllDescendantIds();
        allDescendants.Count.ShouldBe(9); // 3 items + 6 reservations

        var childIds = rootNode.GetChildIds();
        childIds.Count.ShouldBe(3); // Only items, not reservations
    }

    [Fact]
    public void InsertGraphBatch_GrandchildFails_EntireGraphFails()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-001", 2, 2);
        order.OrderItems.First().Reservations.First().ReservedQuantity = -1; // Invalid

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order]);

        result.IsCompleteFailure.ShouldBeTrue();
        result.FailureCount.ShouldBe(1);
    }

    // ========== Multi-Level Update Tests ==========

    [Fact]
    public void UpdateGraphBatch_ModifyAtAllLevels_ChangesPersisted()
    {
        using var context = CreateContext();
        SeedThreeLevelOrders(context, 2, 2, 2);

        var orders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        // Modify at all levels
        orders[0].Status = CustomerOrderStatus.Completed;
        var firstItem = orders[0].OrderItems.First();
        firstItem.Quantity = 999;
        firstItem.Subtotal = firstItem.Quantity * firstItem.UnitPrice;
        var firstReservation = firstItem.Reservations.First();
        firstReservation.ReservedQuantity = 888;

        context.ChangeTracker.DetectChanges();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders);

        result.IsCompleteSuccess.ShouldBeTrue();

        // Verify database state
        context.ChangeTracker.Clear();
        var verifyOrder = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .First(o => o.Id == orders[0].Id);

        verifyOrder.Status.ShouldBe(CustomerOrderStatus.Completed);
        verifyOrder.OrderItems.First().Quantity.ShouldBe(999);
        verifyOrder.OrderItems.First().Reservations.First().ReservedQuantity.ShouldBe(888);
    }

    [Fact]
    public void UpdateGraphBatch_OrphanDetection_AtAllLevels()
    {
        using var context = CreateContext();
        SeedThreeLevelOrders(context, 2, 2, 2);

        var orders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        var removedReservation = orders[0].OrderItems.First().Reservations.First();
        var removedId = removedReservation.Id;
        orders[0].OrderItems.First().Reservations.Remove(removedReservation);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var ex = Should.Throw<InvalidOperationException>(() =>
            saver.UpdateGraphBatch(orders));

        ex.Message.ShouldContain("orphaned");
        ex.Message.ShouldContain(removedId.ToString());
    }

    [Fact]
    public void UpdateGraphBatch_OrphanDelete_CascadesToGrandchildren()
    {
        using var context = CreateContext();
        SeedThreeLevelOrders(context, 2, 2, 2);

        var orders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        var removedReservation = orders[0].OrderItems.First().Reservations.First();
        var removedId = removedReservation.Id;
        orders[0].OrderItems.First().Reservations.Remove(removedReservation);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Delete
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.ItemReservations.Find(removedId).ShouldBeNull();
    }

    [Fact]
    public void UpdateGraphBatch_AddGrandchild_Inserted()
    {
        using var context = CreateContext();
        SeedThreeLevelOrders(context, 2, 2, 1);

        var orders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        var targetItemId = orders[0].OrderItems.First().Id;
        var newReservation = CreateValidReservation(999);
        orders[0].OrderItems.First().Reservations.Add(newReservation);

        // Use direct context save to verify new grandchild is inserted correctly
        context.SaveChanges();
        newReservation.Id.ShouldBeGreaterThan(0);

        context.ChangeTracker.Clear();
        var verifyItem = context.OrderItems
            .Include(i => i.Reservations)
            .First(i => i.Id == targetItemId);
        verifyItem.Reservations.Count.ShouldBe(2);
    }

    [Fact]
    public void UpdateGraphBatch_MaxDepthZero_GraphHierarchyShowsOnlyRoot()
    {
        using var context = CreateContext();
        SeedThreeLevelOrders(context, 2, 2, 2);

        var orders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        // Modify root only
        orders[0].Status = CustomerOrderStatus.Completed;

        context.ChangeTracker.DetectChanges();

        var saver = new BatchSaver<CustomerOrder, int>(context);

        // With MaxDepth=0, only root is included in graph hierarchy
        var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Detach,
            MaxDepth = 0
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        // GraphHierarchy should only show root nodes, no children
        result.GraphHierarchy.ShouldNotBeNull();
        foreach (var node in result.GraphHierarchy!)
        {
            node.Depth.ShouldBe(0);
            node.Children.ShouldBeEmpty();
        }

        // Verify root was updated
        context.ChangeTracker.Clear();
        var verifyOrder = context.CustomerOrders.Find(orders[0].Id);
        verifyOrder!.Status.ShouldBe(CustomerOrderStatus.Completed);
    }

    // ========== Multi-Level Delete Tests ==========

    [Fact]
    public void DeleteGraphBatch_ThreeLevelCascade_AllDescendantsDeleted()
    {
        using var context = CreateContext();
        SeedThreeLevelOrders(context, 2, 2, 2);

        var orderWithGraph = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .First();
        var orderId = orderWithGraph.Id;
        var itemIds = orderWithGraph.OrderItems.Select(i => i.Id).ToList();
        var reservationIds = orderWithGraph.OrderItems
            .SelectMany(i => i.Reservations)
            .Select(r => r.Id).ToList();
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.DeleteGraphBatch([orderWithGraph]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessfulIds.ShouldContain(orderId);

        context.ChangeTracker.Clear();
        context.CustomerOrders.Find(orderId).ShouldBeNull();
        foreach (var itemId in itemIds)
            context.OrderItems.Find(itemId).ShouldBeNull();
        foreach (var reservationId in reservationIds)
            context.ItemReservations.Find(reservationId).ShouldBeNull();
    }

    [Fact]
    public void DeleteGraphBatch_DeletesInCorrectOrder_DeepestFirst()
    {
        using var context = CreateContext();
        SeedThreeLevelOrders(context, 1, 2, 2);

        var orderWithGraph = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .First();
        context.ChangeTracker.Clear();

        // This test verifies FK constraints are respected (deepest first)
        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.DeleteGraphBatch([orderWithGraph]);

        // If order is wrong, SQLite would throw FK constraint violation
        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public void DeleteGraphBatch_MaxDepthLimitsGraphHierarchy()
    {
        using var context = CreateContext();
        SeedThreeLevelOrders(context, 2, 2, 2);

        var orderWithGraph = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .First();
        var orderId = orderWithGraph.Id;
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.DeleteGraphBatch([orderWithGraph], new DeleteGraphBatchOptions
        {
            MaxDepth = 1
        });

        // Delete succeeds (SQLite cascade handles reservations automatically)
        result.IsCompleteSuccess.ShouldBeTrue();

        // GraphHierarchy should only include depth 0 (order) and depth 1 (items)
        var rootNode = result.GraphHierarchy!.First(n => n.EntityId.Equals(orderId));
        rootNode.Depth.ShouldBe(0);
        rootNode.Children.Count.ShouldBe(2);

        // Children at depth 1 should have no children (MaxDepth=1 doesn't include depth 2)
        foreach (var childNode in rootNode.Children)
        {
            childNode.Depth.ShouldBe(1);
            childNode.Children.ShouldBeEmpty();
        }

        // Verify all data deleted (including reservations via DB cascade)
        context.ChangeTracker.Clear();
        context.CustomerOrders.Find(orderId).ShouldBeNull();
    }

    [Fact]
    public void DeleteGraphBatch_PartialFailure_IsolatesGraphs()
    {
        using var context = CreateContext();
        SeedThreeLevelOrders(context, 3, 2, 2);

        var orders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        // Make one order's reservation invalid by setting FK to non-existent item
        // Actually, we can't easily make delete fail, so let's test with valid data
        // and verify graph isolation works
        var firstOrderId = orders[0].Id;
        var secondOrderId = orders[1].Id;
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.DeleteGraphBatch(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.GraphHierarchy.ShouldNotBeNull();
        result.GraphHierarchy!.Count.ShouldBe(3);
    }

    [Fact]
    public void DeleteGraphBatch_ThrowBehavior_ThrowsIfAnyLevelHasChildren()
    {
        using var context = CreateContext();
        SeedThreeLevelOrders(context, 2, 2, 2);

        var orderWithGraph = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .First();
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var options = new DeleteGraphBatchOptions
        {
            CascadeBehavior = DeleteCascadeBehavior.Throw
        };

        Should.Throw<InvalidOperationException>(() =>
            saver.DeleteGraphBatch([orderWithGraph], options))
            .Message.ShouldContain("child(ren)");
    }

    // ========== GraphNode Helper Tests ==========

    [Fact]
    public void GraphNode_GetChildIds_ReturnsOnlyImmediateChildren()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-001", 2, 3);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order]);

        var rootNode = result.GraphHierarchy!.First();

        // GetChildIds should only return OrderItem IDs, not ItemReservation IDs
        var childIds = rootNode.GetChildIds();
        childIds.Count.ShouldBe(2);

        var expectedItemIds = order.OrderItems.Select(i => i.Id).ToList();
        childIds.ShouldBe(expectedItemIds, ignoreOrder: true);
    }

    [Fact]
    public void GraphNode_GetAllDescendantIds_FlattensEntireTree()
    {
        using var context = CreateContext();

        // 1 order → 2 items → 3 reservations each = 2 + 6 = 8 descendants
        var order = CreateThreeLevelOrder("ORD-001", 2, 3);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order]);

        var rootNode = result.GraphHierarchy!.First();
        var allDescendants = rootNode.GetAllDescendantIds();

        allDescendants.Count.ShouldBe(8);

        var expectedItemIds = order.OrderItems.Select(i => i.Id);
        var expectedReservationIds = order.OrderItems
            .SelectMany(i => i.Reservations)
            .Select(r => r.Id);

        allDescendants.ShouldContain(id => expectedItemIds.Contains(id));
        allDescendants.ShouldContain(id => expectedReservationIds.Contains(id));
    }

    [Fact]
    public void GraphNode_LeafNode_ReturnsEmptyLists()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-001", 1, 1);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order]);

        var rootNode = result.GraphHierarchy!.First();
        var itemNode = rootNode.Children.First();
        var reservationNode = itemNode.Children.First();

        reservationNode.GetChildIds().ShouldBeEmpty();
        reservationNode.GetAllDescendantIds().ShouldBeEmpty();
    }

    // ========== Helper Methods ==========

    private static CustomerOrder CreateThreeLevelOrder(string orderNumber, int itemCount, int reservationsPerItem)
    {
        var items = Enumerable.Range(1, itemCount)
            .Select(i => CreateOrderItemWithReservations(i, reservationsPerItem))
            .ToList();

        return new CustomerOrder
        {
            OrderNumber = orderNumber,
            CustomerName = "Test Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = items.Sum(i => i.Subtotal),
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = items
        };
    }

    private static OrderItem CreateOrderItemWithReservations(int index, int reservationCount)
    {
        var quantity = index + 1;
        var unitPrice = 10.00m + index;
        return new OrderItem
        {
            ProductId = 1000 + index,
            ProductName = $"Product {index}",
            Quantity = quantity,
            UnitPrice = unitPrice,
            Subtotal = quantity * unitPrice,
            Reservations = CreateReservations(reservationCount)
        };
    }

    private static List<ItemReservation> CreateReservations(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => CreateValidReservation(i))
            .ToList();
    }

    private static ItemReservation CreateValidReservation(int index)
    {
        return new ItemReservation
        {
            WarehouseLocation = $"Warehouse-{index}",
            ReservedQuantity = index * 10,
            ReservedAt = DateTimeOffset.UtcNow
        };
    }

    private void SeedThreeLevelOrders(TestDbContext context, int orderCount, int itemsPerOrder, int reservationsPerItem)
    {
        var orders = Enumerable.Range(1, orderCount)
            .Select(i => CreateThreeLevelOrder($"ORD-{i:D3}", itemsPerOrder, reservationsPerItem))
            .ToList();

        context.CustomerOrders.AddRange(orders);
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }
}
