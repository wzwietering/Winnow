using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests;

public class SelectiveNavigationFilteringTests : TestBase
{
    // ========== InsertGraphBatch Tests ==========

    [Fact]
    public void InsertGraph_IncludeOnlyOrderItems_ReservationsNotInserted()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-001", 2, 2);
        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order], new InsertGraphBatchOptions
        {
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        order.Id.ShouldBeGreaterThan(0);
        order.OrderItems.ShouldAllBe(item => item.Id > 0);

        // Reservations should NOT be inserted (filtered out)
        order.OrderItems.SelectMany(i => i.Reservations)
            .ShouldAllBe(r => r.Id == 0);
    }

    [Fact]
    public void InsertGraph_ExcludeReservations_OrderAndItemsInserted()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-001", 2, 2);
        var filter = NavigationFilter.Exclude()
            .Navigation<OrderItem>(i => i.Reservations);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order], new InsertGraphBatchOptions
        {
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        order.Id.ShouldBeGreaterThan(0);
        order.OrderItems.ShouldAllBe(item => item.Id > 0);

        // Reservations excluded
        order.OrderItems.SelectMany(i => i.Reservations)
            .ShouldAllBe(r => r.Id == 0);
    }

    [Fact]
    public void InsertGraph_NoFilter_FullGraphInserted()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-001", 2, 2);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order]);

        result.IsCompleteSuccess.ShouldBeTrue();
        order.Id.ShouldBeGreaterThan(0);
        order.OrderItems.ShouldAllBe(item => item.Id > 0);
        order.OrderItems.SelectMany(i => i.Reservations)
            .ShouldAllBe(r => r.Id > 0);
    }

    [Fact]
    public void InsertGraph_IncludeFilter_GraphHierarchyRespectsFilter()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-001", 2, 2);
        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order], new InsertGraphBatchOptions
        {
            NavigationFilter = filter
        });

        result.GraphHierarchy.ShouldNotBeNull();
        var rootNode = result.GraphHierarchy!.First();
        rootNode.Children.Count.ShouldBe(2);

        // No grandchildren in hierarchy (reservations filtered)
        rootNode.Children.ShouldAllBe(c => c.Children.Count == 0);
    }

    // ========== UpdateGraphBatch Tests ==========

    [Fact]
    public void UpdateGraph_IncludeOrderItems_OnlyItemsUpdated()
    {
        using var context = CreateContext();
        SeedThreeLevelOrders(context, 1, 2, 2);

        var orders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        orders[0].Status = CustomerOrderStatus.Completed;
        orders[0].OrderItems.First().Quantity = 999;
        orders[0].OrderItems.First().Subtotal = 999 * orders[0].OrderItems.First().UnitPrice;

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Detach,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var verify = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First(o => o.Id == orders[0].Id);
        verify.Status.ShouldBe(CustomerOrderStatus.Completed);
        verify.OrderItems.First().Quantity.ShouldBe(999);
    }

    [Fact]
    public void UpdateGraph_FilteredNavigation_NoFalseOrphanDetection()
    {
        using var context = CreateContext();
        SeedThreeLevelOrders(context, 1, 2, 2);

        var orders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        // Remove a reservation - but filter excludes reservations from traversal
        orders[0].OrderItems.First().Reservations.Clear();

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);

        // Should NOT throw orphan detection for filtered-out reservations
        var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Throw,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public void UpdateGraph_OrphanDeletion_RespectsFilter()
    {
        using var context = CreateContext();
        SeedThreeLevelOrders(context, 1, 2, 2);

        var orders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        // Remove an order item (which is included in filter)
        var removedItem = orders[0].OrderItems.First();
        var removedItemId = removedItem.Id;
        orders[0].OrderItems.Remove(removedItem);

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Delete,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.OrderItems.Find(removedItemId).ShouldBeNull();
    }

    // ========== DeleteGraphBatch Tests ==========

    [Fact]
    public void DeleteGraph_IncludeOrderItems_DeletesParentAndItems()
    {
        using var context = CreateContext();
        SeedThreeLevelOrders(context, 1, 2, 2);

        var orders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        var orderId = orders[0].Id;
        var itemIds = orders[0].OrderItems.Select(i => i.Id).ToList();

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.DeleteGraphBatch(orders, new DeleteGraphBatchOptions
        {
            CascadeBehavior = DeleteCascadeBehavior.Cascade,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.CustomerOrders.Find(orderId).ShouldBeNull();
        foreach (var itemId in itemIds)
        {
            context.OrderItems.Find(itemId).ShouldBeNull();
        }
    }

    // ========== UpsertGraphBatch Tests ==========

    [Fact]
    public void UpsertGraph_IncludeOrderItems_UpsertsOnlyItems()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-001", 2, 2);

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch([order], new UpsertGraphBatchOptions
        {
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedEntities.Count.ShouldBe(1);
        order.Id.ShouldBeGreaterThan(0);
        order.OrderItems.ShouldAllBe(item => item.Id > 0);

        // Reservations not inserted
        order.OrderItems.SelectMany(i => i.Reservations)
            .ShouldAllBe(r => r.Id == 0);
    }

    // ========== Filter + MaxDepth Combination ==========

    [Fact]
    public void InsertGraph_FilterCombinedWithMaxDepth_BothApplied()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-001", 2, 2);

        // Filter includes everything, but MaxDepth=1 limits to depth 1
        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems)
            .Navigation<OrderItem>(i => i.Reservations);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order], new InsertGraphBatchOptions
        {
            NavigationFilter = filter,
            MaxDepth = 1
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        order.Id.ShouldBeGreaterThan(0);
        order.OrderItems.ShouldAllBe(item => item.Id > 0);

        // MaxDepth=1 limits traversal to depth 1 (items), reservations at depth 2 not reached
        order.OrderItems.SelectMany(i => i.Reservations)
            .ShouldAllBe(r => r.Id == 0);
    }

    // ========== Flag Conflict Validation Tests ==========

    [Fact]
    public void InsertGraph_FilterIncludesReferenceNav_IncludeReferencesFalse_Throws()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-001", 1, 0);

        // Filter includes Product (a reference navigation on OrderItem) but IncludeReferences is false
        var filter = NavigationFilter.Include()
            .Navigation<OrderItem>(i => i.Product);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var ex = Should.Throw<InvalidOperationException>(() =>
            saver.InsertGraphBatch([order], new InsertGraphBatchOptions
            {
                NavigationFilter = filter,
                IncludeReferences = false
            }));

        ex.Message.ShouldContain("IncludeReferences");
        ex.Message.ShouldContain("Product");
    }

    [Fact]
    public void InsertGraph_FilterIncludesManyToManyNav_IncludeManyToManyFalse_Throws()
    {
        using var context = CreateContext();

        var student = new Student { Name = "Test", Email = "test@test.com" };

        var filter = NavigationFilter.Include()
            .Navigation<Student>(s => s.Courses);

        var saver = new BatchSaver<Student, int>(context);
        var ex = Should.Throw<InvalidOperationException>(() =>
            saver.InsertGraphBatch([student], new InsertGraphBatchOptions
            {
                NavigationFilter = filter,
                IncludeManyToMany = false
            }));

        ex.Message.ShouldContain("IncludeManyToMany");
        ex.Message.ShouldContain("Courses");
    }

    // ========== Self-Referencing with Filter ==========

    [Fact]
    public void InsertGraph_SelfReferencing_FilterApplied()
    {
        using var context = CreateContext();

        var parent = new Category
        {
            Name = "Parent",
            SubCategories =
            [
                new Category { Name = "Child1" },
                new Category { Name = "Child2" }
            ]
        };

        var filter = NavigationFilter.Include()
            .Navigation<Category>(c => c.SubCategories);

        var saver = new BatchSaver<Category, int>(context);
        var result = saver.InsertGraphBatch([parent], new InsertGraphBatchOptions
        {
            NavigationFilter = filter,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        parent.Id.ShouldBeGreaterThan(0);
        parent.SubCategories.ShouldAllBe(c => c.Id > 0);
    }

    // ========== Helper Methods ==========

    private static CustomerOrder CreateThreeLevelOrder(
        string orderNumber, int itemCount, int reservationsPerItem)
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
            Reservations = Enumerable.Range(1, reservationCount)
                .Select(i => new ItemReservation
                {
                    WarehouseLocation = $"Warehouse-{i}",
                    ReservedQuantity = i * 10,
                    ReservedAt = DateTimeOffset.UtcNow
                })
                .ToList()
        };
    }

    private void SeedThreeLevelOrders(
        TestDbContext context, int orderCount, int itemsPerOrder, int reservationsPerItem)
    {
        var orders = Enumerable.Range(1, orderCount)
            .Select(i => CreateThreeLevelOrder($"ORD-{i:D3}", itemsPerOrder, reservationsPerItem))
            .ToList();

        context.CustomerOrders.AddRange(orders);
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }
}
