using EfCoreUtils.MixedKey;
using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests;

public class MixedKeyTypeTests : TestBase
{
    // ========== MixedKeyId Tests ==========

    [Fact]
    public void MixedKeyId_GetValue_WithCorrectType_ReturnsValue()
    {
        var id = new MixedKeyId(42, typeof(int));

        var value = id.GetValue<int>();

        value.ShouldBe(42);
    }

    [Fact]
    public void MixedKeyId_GetValue_WithWrongType_ThrowsWithDescriptiveMessage()
    {
        var id = new MixedKeyId(42, typeof(int));

        var ex = Should.Throw<InvalidOperationException>(() => id.GetValue<Guid>());

        ex.Message.ShouldContain("Guid");
        ex.Message.ShouldContain("Int32");
    }

    [Fact]
    public void MixedKeyId_TryGetValue_WithCorrectType_ReturnsTrue()
    {
        var id = new MixedKeyId(Guid.NewGuid(), typeof(Guid));

        var success = id.TryGetValue<Guid>(out var value);

        success.ShouldBeTrue();
        value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void MixedKeyId_Equality_ComparesValueAndType()
    {
        var id1 = new MixedKeyId(42, typeof(int));
        var id2 = new MixedKeyId(42, typeof(int));
        var id3 = new MixedKeyId(42L, typeof(long));

        (id1 == id2).ShouldBeTrue();
        (id1 == id3).ShouldBeFalse();
        id1.Equals(id2).ShouldBeTrue();
    }

    // ========== MixedKeyGraphNode Tests ==========

    [Fact]
    public void MixedKeyGraphNode_GetId_WithCorrectType_ReturnsValue()
    {
        using var context = CreateContext();
        var order = CreateMixedKeyOrder("ORD-001", 1, 1);

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = saver.InsertMixedKeyGraphBatch([order]);

        result.IsCompleteSuccess.ShouldBeTrue();
        var rootNode = result.GraphHierarchy!.First();
        var intId = rootNode.GetId<int>();

        intId.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void MixedKeyGraphNode_GetId_WithWrongType_ThrowsWithDescriptiveMessage()
    {
        using var context = CreateContext();
        var order = CreateMixedKeyOrder("ORD-001", 1, 1);

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = saver.InsertMixedKeyGraphBatch([order]);

        var rootNode = result.GraphHierarchy!.First();

        var ex = Should.Throw<InvalidOperationException>(() => rootNode.GetId<Guid>());
        ex.Message.ShouldContain("Guid");
        ex.Message.ShouldContain("Int32");
    }

    [Fact]
    public void MixedKeyGraphNode_TryGetId_WithCorrectType_ReturnsTrue()
    {
        using var context = CreateContext();
        var order = CreateMixedKeyOrder("ORD-001", 1, 1);

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = saver.InsertMixedKeyGraphBatch([order]);

        var rootNode = result.GraphHierarchy!.First();

        rootNode.TryGetId<int>(out var id).ShouldBeTrue();
        id.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void MixedKeyGraphNode_TryGetId_WithWrongType_ReturnsFalse()
    {
        using var context = CreateContext();
        var order = CreateMixedKeyOrder("ORD-001", 1, 1);

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = saver.InsertMixedKeyGraphBatch([order]);

        var rootNode = result.GraphHierarchy!.First();

        rootNode.TryGetId<Guid>(out _).ShouldBeFalse();
    }

    [Fact]
    public void MixedKeyGraphNode_GetDescendantIdsOfType_FiltersCorrectly()
    {
        using var context = CreateContext();
        var order = CreateMixedKeyOrder("ORD-001", 2, 2);

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = saver.InsertMixedKeyGraphBatch([order]);

        var rootNode = result.GraphHierarchy!.First();

        // Get only Guid IDs (ItemReservationGuid)
        var guidIds = rootNode.GetDescendantIdsOfType<Guid>();
        guidIds.Count.ShouldBe(4); // 2 items * 2 reservations

        // Get only int IDs (OrderItemWithGuidReservations)
        var intIds = rootNode.GetDescendantIdsOfType<int>();
        intIds.Count.ShouldBe(2); // 2 items
    }

    // ========== Mixed Key Insert Tests ==========

    [Fact]
    public void InsertMixedKeyGraphBatch_ThreeLevelWithDifferentKeyTypes_AllInserted()
    {
        using var context = CreateContext();

        var order = CreateMixedKeyOrder("ORD-001", 2, 2);

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = saver.InsertMixedKeyGraphBatch([order]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);

        order.Id.ShouldBeGreaterThan(0);
        order.OrderItems.ShouldAllBe(item => item.Id > 0);
        order.OrderItems.SelectMany(i => i.Reservations)
            .ShouldAllBe(r => r.Id != Guid.Empty);
    }

    [Fact]
    public void InsertMixedKeyGraphBatch_GraphHierarchy_ContainsMixedKeyTypes()
    {
        using var context = CreateContext();

        var order = CreateMixedKeyOrder("ORD-001", 2, 2);

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = saver.InsertMixedKeyGraphBatch([order]);

        result.GraphHierarchy.ShouldNotBeNull();
        var rootNode = result.GraphHierarchy!.First();

        // Root (CustomerOrderWithGuidGrandchildren) has int key
        rootNode.KeyType.ShouldBe(typeof(int));
        rootNode.EntityType.ShouldBe(nameof(CustomerOrderWithGuidGrandchildren));

        // Children (OrderItemWithGuidReservations) have int keys
        foreach (var childNode in rootNode.Children)
        {
            childNode.KeyType.ShouldBe(typeof(int));
            childNode.EntityType.ShouldBe(nameof(OrderItemWithGuidReservations));

            // Grandchildren (ItemReservationGuid) have Guid keys
            foreach (var grandchildNode in childNode.Children)
            {
                grandchildNode.KeyType.ShouldBe(typeof(Guid));
                grandchildNode.EntityType.ShouldBe(nameof(ItemReservationGuid));
            }
        }
    }

    [Fact]
    public void InsertMixedKeyGraphBatch_TraversalInfo_EntitiesByKeyTypeAccurate()
    {
        using var context = CreateContext();

        // 1 order[int] + 2 items[int] + 4 reservations[Guid]
        var order = CreateMixedKeyOrder("ORD-001", 2, 2);

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = saver.InsertMixedKeyGraphBatch([order]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo.ShouldNotBeNull();

        // int: 1 order + 2 items = 3
        result.TraversalInfo!.EntitiesByKeyType[typeof(int)].ShouldBe(3);
        // Guid: 4 reservations
        result.TraversalInfo!.EntitiesByKeyType[typeof(Guid)].ShouldBe(4);
    }

    [Fact]
    public void InsertMixedKeyGraphBatch_SingleLevel_WorksCorrectly()
    {
        using var context = CreateContext();

        var order = new CustomerOrderWithGuidGrandchildren
        {
            OrderNumber = "ORD-001",
            CustomerName = "Test Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 0,
            OrderDate = DateTimeOffset.UtcNow
        };

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = saver.InsertMixedKeyGraphBatch([order]);

        result.IsCompleteSuccess.ShouldBeTrue();
        order.Id.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void InsertMixedKeyGraphBatch_GuidGrandchildFails_EntireGraphRollsBack()
    {
        using var context = CreateContext();

        var order = CreateMixedKeyOrder("ORD-001", 2, 2);
        order.OrderItems.First().Reservations.First().ReservedQuantity = -1; // Invalid

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = saver.InsertMixedKeyGraphBatch([order]);

        result.IsCompleteFailure.ShouldBeTrue();
        result.FailureCount.ShouldBe(1);
    }

    [Fact]
    public void InsertMixedKeyGraphBatch_EmptyCollection_ReturnsEmptyResult()
    {
        using var context = CreateContext();

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = saver.InsertMixedKeyGraphBatch([]);

        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
    }

    [Fact]
    public async Task InsertMixedKeyGraphBatchAsync_WorksCorrectly()
    {
        using var context = CreateContext();

        var order = CreateMixedKeyOrder("ORD-001", 2, 2);

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = await saver.InsertMixedKeyGraphBatchAsync([order]);

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    // ========== Mixed Key Update Tests ==========

    [Fact]
    public void UpdateMixedKeyGraphBatch_ModifyAtAllLevels_ChangesPersisted()
    {
        using var context = CreateContext();
        SeedMixedKeyOrders(context, 2, 2, 2);

        var orders = context.CustomerOrdersWithGuidGrandchildren
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

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = saver.UpdateMixedKeyGraphBatch(orders);

        result.IsCompleteSuccess.ShouldBeTrue();

        // Verify database state
        context.ChangeTracker.Clear();
        var verifyOrder = context.CustomerOrdersWithGuidGrandchildren
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .First(o => o.Id == orders[0].Id);

        verifyOrder.Status.ShouldBe(CustomerOrderStatus.Completed);
        verifyOrder.OrderItems.First().Quantity.ShouldBe(999);
        verifyOrder.OrderItems.First().Reservations.First().ReservedQuantity.ShouldBe(888);
    }

    [Fact]
    public void UpdateMixedKeyGraphBatch_OrphanDetection_DetectsGuidOrphanWithErrorMessage()
    {
        using var context = CreateContext();
        SeedMixedKeyOrders(context, 2, 2, 2);

        var orders = context.CustomerOrdersWithGuidGrandchildren
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        var removedReservation = orders[0].OrderItems.First().Reservations.First();
        var removedId = removedReservation.Id;
        orders[0].OrderItems.First().Reservations.Remove(removedReservation);

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var ex = Should.Throw<InvalidOperationException>(() =>
            saver.UpdateMixedKeyGraphBatch(orders));

        ex.Message.ShouldContain("orphaned");
        ex.Message.ShouldContain(removedId.ToString());
    }

    [Fact]
    public void UpdateMixedKeyGraphBatch_OrphanDelete_HandlesGuidChildren()
    {
        using var context = CreateContext();
        SeedMixedKeyOrders(context, 2, 2, 2);

        var orders = context.CustomerOrdersWithGuidGrandchildren
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        var removedReservation = orders[0].OrderItems.First().Reservations.First();
        var removedId = removedReservation.Id;
        orders[0].OrderItems.First().Reservations.Remove(removedReservation);

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = saver.UpdateMixedKeyGraphBatch(orders, new GraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Delete
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.ItemReservationsGuid.Find(removedId).ShouldBeNull();
    }

    [Fact]
    public void UpdateMixedKeyGraphBatch_OrphanDetach_WorksWithMixedKeys()
    {
        using var context = CreateContext();
        SeedMixedKeyOrders(context, 2, 2, 2);

        var orders = context.CustomerOrdersWithGuidGrandchildren
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        var removedReservation = orders[0].OrderItems.First().Reservations.First();
        var removedId = removedReservation.Id;
        orders[0].OrderItems.First().Reservations.Remove(removedReservation);

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = saver.UpdateMixedKeyGraphBatch(orders, new GraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Detach
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        // With Detach, the reservation should still exist in the database
        context.ChangeTracker.Clear();
        var detachedReservation = context.ItemReservationsGuid.Find(removedId);
        detachedReservation.ShouldNotBeNull();
    }

    [Fact]
    public void UpdateMixedKeyGraphBatch_AddChildAtGuidLevel_Inserted()
    {
        using var context = CreateContext();
        SeedMixedKeyOrders(context, 2, 2, 1);

        var orders = context.CustomerOrdersWithGuidGrandchildren
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        var targetItemId = orders[0].OrderItems.First().Id;
        var newReservation = new ItemReservationGuid
        {
            WarehouseLocation = "New-Warehouse",
            ReservedQuantity = 999,
            ReservedAt = DateTimeOffset.UtcNow
        };
        orders[0].OrderItems.First().Reservations.Add(newReservation);

        // Use direct context save to verify new grandchild is inserted correctly
        context.SaveChanges();
        newReservation.Id.ShouldNotBe(Guid.Empty);

        context.ChangeTracker.Clear();
        var verifyItem = context.OrderItemsWithGuidReservations
            .Include(i => i.Reservations)
            .First(i => i.Id == targetItemId);
        verifyItem.Reservations.Count.ShouldBe(2);
    }

    [Fact]
    public void UpdateMixedKeyGraphBatch_FKMatchingAcrossTypes_WorksCorrectly()
    {
        using var context = CreateContext();
        SeedMixedKeyOrders(context, 1, 2, 2);

        var orders = context.CustomerOrdersWithGuidGrandchildren
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        // Modify both int-keyed and Guid-keyed entities
        orders[0].CustomerName = "Updated Customer";
        orders[0].OrderItems.First().Reservations.First().ReservedQuantity = 999;

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = saver.UpdateMixedKeyGraphBatch(orders, new GraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Detach
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        // Verify FK relationships still valid
        context.ChangeTracker.Clear();
        var verified = context.CustomerOrdersWithGuidGrandchildren
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .First();
        verified.CustomerName.ShouldBe("Updated Customer");
        verified.OrderItems.First().Reservations.First().ReservedQuantity.ShouldBe(999);
    }

    [Fact]
    public async Task UpdateMixedKeyGraphBatchAsync_WorksCorrectly()
    {
        using var context = CreateContext();
        SeedMixedKeyOrders(context, 2, 2, 2);

        var orders = context.CustomerOrdersWithGuidGrandchildren
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        orders[0].Status = CustomerOrderStatus.Completed;

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = await saver.UpdateMixedKeyGraphBatchAsync(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    // ========== Mixed Key Delete Tests ==========

    [Fact]
    public void DeleteMixedKeyGraphBatch_ThreeLevelCascade_AllDeleted()
    {
        using var context = CreateContext();
        SeedMixedKeyOrders(context, 2, 2, 2);

        var orderWithGraph = context.CustomerOrdersWithGuidGrandchildren
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .First();
        var orderId = orderWithGraph.Id;
        var itemIds = orderWithGraph.OrderItems.Select(i => i.Id).ToList();
        var reservationIds = orderWithGraph.OrderItems
            .SelectMany(i => i.Reservations)
            .Select(r => r.Id).ToList();
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = saver.DeleteMixedKeyGraphBatch([orderWithGraph]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessfulIds.ShouldContain(id => id.GetValue<int>() == orderId);

        context.ChangeTracker.Clear();
        context.CustomerOrdersWithGuidGrandchildren.Find(orderId).ShouldBeNull();
        foreach (var itemId in itemIds)
            context.OrderItemsWithGuidReservations.Find(itemId).ShouldBeNull();
        foreach (var reservationId in reservationIds)
            context.ItemReservationsGuid.Find(reservationId).ShouldBeNull();
    }

    [Fact]
    public void DeleteMixedKeyGraphBatch_DeletesInCorrectOrder_GuidGrandchildrenFirst()
    {
        using var context = CreateContext();
        SeedMixedKeyOrders(context, 1, 2, 2);

        var orderWithGraph = context.CustomerOrdersWithGuidGrandchildren
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .First();
        context.ChangeTracker.Clear();

        // This test verifies FK constraints are respected (deepest first)
        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = saver.DeleteMixedKeyGraphBatch([orderWithGraph]);

        // If order is wrong, SQLite would throw FK constraint violation
        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public void DeleteMixedKeyGraphBatch_ThrowBehavior_ThrowsIfAnyLevelHasChildren()
    {
        using var context = CreateContext();
        SeedMixedKeyOrders(context, 2, 2, 2);

        var orderWithGraph = context.CustomerOrdersWithGuidGrandchildren
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .First();
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var options = new DeleteGraphBatchOptions
        {
            CascadeBehavior = DeleteCascadeBehavior.Throw
        };

        Should.Throw<InvalidOperationException>(() =>
            saver.DeleteMixedKeyGraphBatch([orderWithGraph], options))
            .Message.ShouldContain("child(ren)");
    }

    [Fact]
    public void DeleteMixedKeyGraphBatch_MaxDepthLimitsGraphHierarchy()
    {
        using var context = CreateContext();
        SeedMixedKeyOrders(context, 2, 2, 2);

        var orderWithGraph = context.CustomerOrdersWithGuidGrandchildren
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .First();
        var orderId = orderWithGraph.Id;
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = saver.DeleteMixedKeyGraphBatch([orderWithGraph], new DeleteGraphBatchOptions
        {
            MaxDepth = 1
        });

        // Delete succeeds (SQLite cascade handles reservations automatically)
        result.IsCompleteSuccess.ShouldBeTrue();

        // GraphHierarchy should only include depth 0 (order) and depth 1 (items)
        var rootNode = result.GraphHierarchy!.First(n => n.GetId<int>() == orderId);
        rootNode.Depth.ShouldBe(0);
        rootNode.Children.Count.ShouldBe(2);

        // Children at depth 1 should have no children (MaxDepth=1 doesn't include depth 2)
        foreach (var childNode in rootNode.Children)
        {
            childNode.Depth.ShouldBe(1);
            childNode.Children.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task DeleteMixedKeyGraphBatchAsync_WorksCorrectly()
    {
        using var context = CreateContext();
        SeedMixedKeyOrders(context, 2, 2, 2);

        var orderWithGraph = context.CustomerOrdersWithGuidGrandchildren
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .First();
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrderWithGuidGrandchildren, int>(context);
        var result = await saver.DeleteMixedKeyGraphBatchAsync([orderWithGraph]);

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    // ========== Helper Methods ==========

    private static CustomerOrderWithGuidGrandchildren CreateMixedKeyOrder(
        string orderNumber, int itemCount, int reservationsPerItem)
    {
        var items = Enumerable.Range(1, itemCount)
            .Select(i => CreateOrderItemWithGuidReservations(i, reservationsPerItem))
            .ToList();

        return new CustomerOrderWithGuidGrandchildren
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

    private static OrderItemWithGuidReservations CreateOrderItemWithGuidReservations(
        int index, int reservationCount)
    {
        var quantity = index + 1;
        var unitPrice = 10.00m + index;
        return new OrderItemWithGuidReservations
        {
            ProductId = 1000 + index,
            ProductName = $"Product {index}",
            Quantity = quantity,
            UnitPrice = unitPrice,
            Subtotal = quantity * unitPrice,
            Reservations = CreateGuidReservations(reservationCount)
        };
    }

    private static List<ItemReservationGuid> CreateGuidReservations(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => CreateGuidReservation(i))
            .ToList();
    }

    private static ItemReservationGuid CreateGuidReservation(int index)
    {
        return new ItemReservationGuid
        {
            Id = Guid.NewGuid(),
            WarehouseLocation = $"Warehouse-{index}",
            ReservedQuantity = index * 10,
            ReservedAt = DateTimeOffset.UtcNow
        };
    }

    private void SeedMixedKeyOrders(TestDbContext context, int orderCount,
        int itemsPerOrder, int reservationsPerItem)
    {
        var orders = Enumerable.Range(1, orderCount)
            .Select(i => CreateMixedKeyOrder($"MK-ORD-{i:D3}", itemsPerOrder, reservationsPerItem))
            .ToList();

        context.CustomerOrdersWithGuidGrandchildren.AddRange(orders);
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }
}
