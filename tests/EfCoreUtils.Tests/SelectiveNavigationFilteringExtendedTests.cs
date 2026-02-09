using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests;

// ========== Async Operations ==========

public class SelectiveFilteringAsyncTests : TestBase
{
    [Fact]
    public async Task InsertGraphBatchAsync_WithFilter_CorrectBehavior()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-A-001", 2, 2);
        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = await saver.InsertGraphBatchAsync([order], new InsertGraphBatchOptions
        {
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        order.OrderItems.ShouldAllBe(item => item.Id > 0);
        order.OrderItems.SelectMany(i => i.Reservations).ShouldAllBe(r => r.Id == 0);
    }

    [Fact]
    public async Task UpdateGraphBatchAsync_WithFilter_OrphanDetection()
    {
        using var context = CreateContext();
        SeedThreeLevelOrders(context, 1, 2, 2);

        var orders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        orders[0].OrderItems.First().Reservations.Clear();

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = await saver.UpdateGraphBatchAsync(orders, new GraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Throw,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteGraphBatchAsync_WithFilter_CascadeRespected()
    {
        using var context = CreateContext();
        SeedThreeLevelOrders(context, 1, 2, 2);

        var orders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ToList();
        var orderId = orders[0].Id;

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = await saver.DeleteGraphBatchAsync(orders, new DeleteGraphBatchOptions
        {
            CascadeBehavior = DeleteCascadeBehavior.Cascade,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        context.ChangeTracker.Clear();
        context.CustomerOrders.Find(orderId).ShouldBeNull();
    }

    [Fact]
    public async Task UpsertGraphBatchAsync_WithFilter_InsertsAndUpdates()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-A-002", 2, 2);
        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = await saver.UpsertGraphBatchAsync([order], new UpsertGraphBatchOptions
        {
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        order.OrderItems.ShouldAllBe(item => item.Id > 0);
        order.OrderItems.SelectMany(i => i.Reservations).ShouldAllBe(r => r.Id == 0);
    }

    #region Helpers

    private static CustomerOrder CreateThreeLevelOrder(
        string orderNumber, int itemCount, int reservationsPerItem)
    {
        return new CustomerOrder
        {
            OrderNumber = orderNumber,
            CustomerName = "Test Customer", CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 100m, OrderDate = DateTimeOffset.UtcNow,
            OrderItems = Enumerable.Range(1, itemCount).Select(i => new OrderItem
            {
                ProductId = 1000 + i, ProductName = $"Product {i}",
                Quantity = i + 1, UnitPrice = 10m + i, Subtotal = (i + 1) * (10m + i),
                Reservations = Enumerable.Range(1, reservationsPerItem).Select(j => new ItemReservation
                {
                    WarehouseLocation = $"WH-{j}", ReservedQuantity = j * 10,
                    ReservedAt = DateTimeOffset.UtcNow
                }).ToList()
            }).ToList()
        };
    }

    private void SeedThreeLevelOrders(TestDbContext context, int count, int items, int reservations)
    {
        var orders = Enumerable.Range(1, count)
            .Select(i => CreateThreeLevelOrder($"ORD-S-{i:D3}", items, reservations))
            .ToList();
        context.CustomerOrders.AddRange(orders);
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }

    #endregion
}

// ========== DivideAndConquer Strategy ==========

public class SelectiveFilteringDivideAndConquerTests : TestBase
{
    [Fact]
    public void InsertGraph_DivideAndConquer_FilterAppliedPerChunk()
    {
        using var context = CreateContext();

        var orders = Enumerable.Range(1, 4).Select(i => CreateOrderWithReservations($"ORD-DC-{i}")).ToList();
        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch(orders, new InsertGraphBatchOptions
        {
            Strategy = BatchStrategy.DivideAndConquer,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        context.ItemReservations.Count().ShouldBe(0);
    }

    [Fact]
    public void UpdateGraph_DivideAndConquer_FilterApplied()
    {
        using var context = CreateContext();
        SeedOrders(context, 4);

        var orders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        orders[0].Status = CustomerOrderStatus.Completed;
        orders[0].OrderItems.First().Reservations.Clear();

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
        {
            Strategy = BatchStrategy.DivideAndConquer,
            OrphanedChildBehavior = OrphanBehavior.Throw,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public void UpsertGraph_DivideAndConquer_FilterApplied()
    {
        using var context = CreateContext();

        var orders = Enumerable.Range(1, 4).Select(i => CreateOrderWithReservations($"ORD-DCU-{i}")).ToList();
        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch(orders, new UpsertGraphBatchOptions
        {
            Strategy = BatchStrategy.DivideAndConquer,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        context.ItemReservations.Count().ShouldBe(0);
    }

    [Fact]
    public void DeleteGraph_DivideAndConquer_FilterApplied()
    {
        using var context = CreateContext();
        SeedOrders(context, 4);

        var orders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ToList();

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.DeleteGraphBatch(orders, new DeleteGraphBatchOptions
        {
            Strategy = BatchStrategy.DivideAndConquer,
            CascadeBehavior = DeleteCascadeBehavior.Cascade,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(0);
    }

    #region Helpers

    private static CustomerOrder CreateOrderWithReservations(string orderNumber) => new()
    {
        OrderNumber = orderNumber,
        CustomerName = "Test", CustomerId = 1,
        Status = CustomerOrderStatus.Pending,
        TotalAmount = 22m, OrderDate = DateTimeOffset.UtcNow,
        OrderItems =
        [
            new OrderItem
            {
                ProductId = 1000, ProductName = "Product", Quantity = 2,
                UnitPrice = 11m, Subtotal = 22m,
                Reservations = [new ItemReservation { WarehouseLocation = "WH-1", ReservedQuantity = 1, ReservedAt = DateTimeOffset.UtcNow }]
            }
        ]
    };

    private void SeedOrders(TestDbContext context, int count)
    {
        var orders = Enumerable.Range(1, count).Select(i => CreateOrderWithReservations($"ORD-S-{i}")).ToList();
        context.CustomerOrders.AddRange(orders);
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }

    #endregion
}

// ========== Orphan Detection Edge Cases ==========

public class SelectiveFilteringOrphanEdgeCaseTests : TestBase
{
    [Fact]
    public void UpdateGraph_RemoveFilteredChild_NoOrphanException()
    {
        using var context = CreateContext();
        SeedThreeLevelOrders(context);

        var orders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        orders[0].OrderItems.First().Reservations.Clear();

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Throw,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public void UpdateGraph_RemoveIncludedChild_DetachBehavior()
    {
        using var context = CreateContext();
        SeedThreeLevelOrders(context);

        var orders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ToList();

        var removedItem = orders[0].OrderItems.First();
        var removedItemId = removedItem.Id;
        orders[0].OrderItems.Remove(removedItem);

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
        context.OrderItems.Find(removedItemId).ShouldNotBeNull();
    }

    [Fact]
    public void UpdateGraph_OrphanAtDepth2_FilterAtDepth1_NoException()
    {
        using var context = CreateContext();
        SeedThreeLevelOrders(context);

        var orders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        var item = orders[0].OrderItems.First();
        item.Reservations.Clear();

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Throw,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public void UpdateGraph_MultiLevelOrphan_SelectiveFilter_CorrectBehavior()
    {
        using var context = CreateContext();
        SeedThreeLevelOrders(context);

        var orders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        var removedItem = orders[0].OrderItems.First();
        var removedItemId = removedItem.Id;
        orders[0].OrderItems.Remove(removedItem);

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems)
            .Navigation<OrderItem>(i => i.Reservations);

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

    #region Helpers

    private void SeedThreeLevelOrders(TestDbContext context)
    {
        var order = new CustomerOrder
        {
            OrderNumber = "ORD-ORP-001",
            CustomerName = "Test", CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 44m, OrderDate = DateTimeOffset.UtcNow,
            OrderItems = Enumerable.Range(1, 2).Select(i => new OrderItem
            {
                ProductId = 1000 + i, ProductName = $"Product {i}",
                Quantity = 2, UnitPrice = 11m, Subtotal = 22m,
                Reservations = [new ItemReservation { WarehouseLocation = $"WH-{i}", ReservedQuantity = 1, ReservedAt = DateTimeOffset.UtcNow }]
            }).ToList()
        };
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }

    #endregion
}

// ========== Filter Validation Edge Cases ==========

public class SelectiveFilteringValidationEdgeCaseTests : TestBase
{
    [Fact]
    public void Validation_FilterIncludesCollection_IncludeReferencesFalse_NoError()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-V-001",
            CustomerName = "Test", CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 0, OrderDate = DateTimeOffset.UtcNow,
            OrderItems = [new OrderItem { ProductId = 1000, ProductName = "P", Quantity = 1, UnitPrice = 1, Subtotal = 1 }]
        };

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order], new InsertGraphBatchOptions
        {
            IncludeReferences = false,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Validation_FilterIncludesNonExistentNav_Throws()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-V-002",
            CustomerName = "Test", CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 0, OrderDate = DateTimeOffset.UtcNow
        };

        // Name property exists but is not a navigation - should throw
        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderNumber);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var ex = Should.Throw<InvalidOperationException>(() =>
            saver.InsertGraphBatch([order], new InsertGraphBatchOptions
            {
                NavigationFilter = filter
            }));

        ex.Message.ShouldContain("OrderNumber");
        ex.Message.ShouldContain("no such navigation");
    }

    [Fact]
    public void Validation_ExcludeMode_NonExistentNav_Throws()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-V-003",
            CustomerName = "Test", CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 0, OrderDate = DateTimeOffset.UtcNow
        };

        var filter = NavigationFilter.Exclude()
            .Navigation<CustomerOrder>(o => o.OrderNumber);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var ex = Should.Throw<InvalidOperationException>(() =>
            saver.InsertGraphBatch([order], new InsertGraphBatchOptions
            {
                NavigationFilter = filter
            }));

        ex.Message.ShouldContain("OrderNumber");
        ex.Message.ShouldContain("no such navigation");
    }

    [Fact]
    public void Validation_CaseSensitiveNavName_MatchesExactly()
    {
        NavigationFilter filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        filter.ShouldTraverse(typeof(CustomerOrder), "OrderItems").ShouldBeTrue();
        filter.ShouldTraverse(typeof(CustomerOrder), "orderitems").ShouldBeFalse();
        filter.ShouldTraverse(typeof(CustomerOrder), "ORDERITEMS").ShouldBeFalse();
    }
}

// ========== Multi-Level Filtering ==========

public class SelectiveFilteringMultiLevelTests : TestBase
{
    [Fact]
    public void InsertGraph_IncludeLevel0ExcludeLevel1_CorrectTraversal()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-ML-001");
        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order], new InsertGraphBatchOptions
        {
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        order.OrderItems.ShouldAllBe(i => i.Id > 0);
        order.OrderItems.SelectMany(i => i.Reservations).ShouldAllBe(r => r.Id == 0);
    }

    [Fact]
    public void InsertGraph_NoRulesAtLevel0_StrictModeBlocksAll()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-ML-002");

        // Filter only has rules for OrderItem, not CustomerOrder
        // In include mode, CustomerOrder has no rules → no navigations traversed
        var filter = NavigationFilter.Include()
            .Navigation<OrderItem>(i => i.Reservations);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order], new InsertGraphBatchOptions
        {
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        order.Id.ShouldBeGreaterThan(0);
        order.OrderItems.ShouldAllBe(i => i.Id == 0);
    }

    [Fact]
    public void InsertGraph_ExcludeAtMultipleLevels_AllRespected()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-ML-003");
        var filter = NavigationFilter.Exclude()
            .Navigation<OrderItem>(i => i.Reservations);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order], new InsertGraphBatchOptions
        {
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        order.OrderItems.ShouldAllBe(i => i.Id > 0);
        order.OrderItems.SelectMany(i => i.Reservations).ShouldAllBe(r => r.Id == 0);
    }

    [Fact]
    public void InsertGraph_FilterCreatesGap_Level0And2Included_Level1Excluded()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-ML-004");

        // Include OrderItems from CustomerOrder, but no rules for OrderItem
        // In strict include mode, OrderItem has no rules → reservations won't be traversed
        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order], new InsertGraphBatchOptions
        {
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        order.OrderItems.ShouldAllBe(i => i.Id > 0);
        order.OrderItems.SelectMany(i => i.Reservations).ShouldAllBe(r => r.Id == 0);
    }

    #region Helpers

    private static CustomerOrder CreateThreeLevelOrder(string orderNumber) => new()
    {
        OrderNumber = orderNumber,
        CustomerName = "Test", CustomerId = 1,
        Status = CustomerOrderStatus.Pending,
        TotalAmount = 44m, OrderDate = DateTimeOffset.UtcNow,
        OrderItems = Enumerable.Range(1, 2).Select(i => new OrderItem
        {
            ProductId = 1000 + i, ProductName = $"Product {i}",
            Quantity = 2, UnitPrice = 11m, Subtotal = 22m,
            Reservations = [new ItemReservation { WarehouseLocation = $"WH-{i}", ReservedQuantity = 1, ReservedAt = DateTimeOffset.UtcNow }]
        }).ToList()
    };

    #endregion
}

// ========== Self-Referencing Extended ==========

public class SelectiveFilteringSelfReferencingExtendedTests : TestBase
{
    [Fact]
    public void InsertGraph_SelfRef_ExcludeFilter_OnlyRootInserted()
    {
        using var context = CreateContext();

        var root = new Category
        {
            Name = "Root",
            SubCategories = [new Category { Name = "Child" }]
        };

        var filter = NavigationFilter.Exclude()
            .Navigation<Category>(c => c.SubCategories);

        var saver = new BatchSaver<Category, int>(context);
        var result = saver.InsertGraphBatch([root], new InsertGraphBatchOptions
        {
            NavigationFilter = filter,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        root.Id.ShouldBeGreaterThan(0);

        context.ChangeTracker.Clear();
        context.Categories.Count().ShouldBe(1);
    }

    [Fact]
    public void InsertGraph_SelfRef_BothDirections_FilterControls()
    {
        using var context = CreateContext();

        var parent = new Category { Name = "Parent" };
        context.Categories.Add(parent);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var child = new Category
        {
            Name = "Child",
            ParentCategoryId = parent.Id,
            SubCategories = [new Category { Name = "Grandchild" }]
        };

        var filter = NavigationFilter.Include()
            .Navigation<Category>(c => c.SubCategories);

        var saver = new BatchSaver<Category, int>(context);
        var result = saver.InsertGraphBatch([child], new InsertGraphBatchOptions
        {
            NavigationFilter = filter,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.Categories.Count().ShouldBe(3);
    }

    [Fact]
    public void InsertGraph_SelfRef_DeepHierarchy_MaxDepthWinsOverFilter()
    {
        using var context = CreateContext();

        var root = new Category
        {
            Name = "L0",
            SubCategories =
            [
                new Category
                {
                    Name = "L1",
                    SubCategories = [new Category { Name = "L2" }]
                }
            ]
        };

        var filter = NavigationFilter.Include()
            .Navigation<Category>(c => c.SubCategories);

        var saver = new BatchSaver<Category, int>(context);
        var result = saver.InsertGraphBatch([root], new InsertGraphBatchOptions
        {
            NavigationFilter = filter,
            MaxDepth = 1,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.Categories.Count().ShouldBe(2);
    }

    [Fact]
    public void UpdateGraph_SelfRef_FilterWithCircularRefHandling()
    {
        using var context = CreateContext();

        var root = new Category { Name = "Root" };
        var child = new Category { Name = "Child" };
        root.SubCategories = [child];
        context.Categories.Add(root);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loaded = context.Categories
            .Include(c => c.SubCategories)
            .First(c => c.Name == "Root");
        loaded.Description = "Updated Root";

        var filter = NavigationFilter.Include()
            .Navigation<Category>(c => c.SubCategories);

        var saver = new BatchSaver<Category, int>(context);
        var result = saver.UpdateGraphBatch([loaded], new GraphBatchOptions
        {
            CircularReferenceHandling = CircularReferenceHandling.Ignore,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var verify = context.Categories.First(c => c.Name == "Root");
        verify.Description.ShouldBe("Updated Root");
    }
}

// ========== Edge Case Coverage ==========

public class SelectiveFilteringEdgeCaseTests : TestBase
{
    [Fact]
    public void InsertGraph_EmptyCollectionWithFilter_Succeeds()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-EC-001",
            CustomerName = "Test", CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 0, OrderDate = DateTimeOffset.UtcNow,
            OrderItems = []
        };

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order], new InsertGraphBatchOptions
        {
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        order.Id.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void InsertGraph_NullReferenceNavWithFilter_Succeeds()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-EC-002",
            CustomerName = "Test", CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 11m, OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = 1000, ProductName = "P",
                    Quantity = 1, UnitPrice = 11m, Subtotal = 11m,
                    Product = null
                }
            ]
        };

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems)
            .Navigation<OrderItem>(i => i.Product);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order], new InsertGraphBatchOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        order.OrderItems.First().Id.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void UpdateGraph_OrphanBehaviorDelete_WithExcludeFilter_Works()
    {
        using var context = CreateContext();
        SeedThreeLevelOrder(context);

        var orders = context.CustomerOrders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.Reservations)
            .ToList();

        var reservation = orders[0].OrderItems.First().Reservations.First();
        var reservationId = reservation.Id;
        orders[0].OrderItems.First().Reservations.Remove(reservation);

        // Exclude filter skips OrderItems nav, but Reservations are still tracked
        var filter = NavigationFilter.Exclude()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpdateGraphBatch(orders, new GraphBatchOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Delete,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public void FilterReuse_AcrossMultipleOperations_Succeeds()
    {
        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems)
            .Build();

        // First operation: InsertGraph
        using var context1 = CreateContext();
        var order1 = CreateOrder("ORD-EC-004");
        var saver1 = new BatchSaver<CustomerOrder, int>(context1);
        var result1 = saver1.InsertGraphBatch([order1], new InsertGraphBatchOptions
        {
            NavigationFilter = filter
        });
        result1.IsCompleteSuccess.ShouldBeTrue();

        // Second operation: InsertGraph on separate context with same filter
        using var context2 = CreateContext();
        var order2 = CreateOrder("ORD-EC-005");
        var saver2 = new BatchSaver<CustomerOrder, int>(context2);
        var result2 = saver2.InsertGraphBatch([order2], new InsertGraphBatchOptions
        {
            NavigationFilter = filter
        });
        result2.IsCompleteSuccess.ShouldBeTrue();

        // Filter state is unchanged (still works for include mode)
        filter.IsIncludeMode.ShouldBeTrue();
        filter.ShouldTraverse(typeof(CustomerOrder), "OrderItems").ShouldBeTrue();
    }

    [Fact]
    public void InsertGraph_CircularRef_ExcludeFilterBlocksCircularPath_NoThrow()
    {
        using var context = CreateContext();

        var parent = new Category { Name = "Parent" };
        var child = new Category { Name = "Child", ParentCategory = parent };
        parent.SubCategories = [child];

        var filter = NavigationFilter.Exclude()
            .Navigation<Category>(c => c.SubCategories);

        var saver = new BatchSaver<Category, int>(context);

        // CircularReferenceHandling.Throw should NOT throw because the filter
        // blocks the SubCategories path that would create the cycle
        var result = saver.InsertGraphBatch([parent], new InsertGraphBatchOptions
        {
            CircularReferenceHandling = CircularReferenceHandling.Throw,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        parent.Id.ShouldBeGreaterThan(0);

        context.ChangeTracker.Clear();
        context.Categories.Count().ShouldBe(1);
    }

    [Fact]
    public void InsertGraph_DeepIncludeFilter_AllLevels_FullGraphInserted()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-EC-006",
            CustomerName = "Test", CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 22m, OrderDate = DateTimeOffset.UtcNow,
            OrderItems = Enumerable.Range(1, 2).Select(i => new OrderItem
            {
                ProductId = 1000 + i, ProductName = $"Product {i}",
                Quantity = 2, UnitPrice = 11m, Subtotal = 22m,
                Reservations =
                [
                    new ItemReservation
                    {
                        WarehouseLocation = $"WH-{i}",
                        ReservedQuantity = 10,
                        ReservedAt = DateTimeOffset.UtcNow
                    }
                ]
            }).ToList()
        };

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems)
            .Navigation<OrderItem>(i => i.Reservations);

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.InsertGraphBatch([order], new InsertGraphBatchOptions
        {
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        order.Id.ShouldBeGreaterThan(0);
        order.OrderItems.ShouldAllBe(i => i.Id > 0);
        order.OrderItems.SelectMany(i => i.Reservations).ShouldAllBe(r => r.Id > 0);
    }

    #region Helpers

    private static CustomerOrder CreateOrder(string orderNumber) => new()
    {
        OrderNumber = orderNumber,
        CustomerName = "Test", CustomerId = 1,
        Status = CustomerOrderStatus.Pending,
        TotalAmount = 22m, OrderDate = DateTimeOffset.UtcNow,
        OrderItems =
        [
            new OrderItem
            {
                ProductId = 1000, ProductName = "Product",
                Quantity = 2, UnitPrice = 11m, Subtotal = 22m,
                Reservations =
                [
                    new ItemReservation
                    {
                        WarehouseLocation = "WH-1",
                        ReservedQuantity = 10,
                        ReservedAt = DateTimeOffset.UtcNow
                    }
                ]
            }
        ]
    };

    private void SeedThreeLevelOrder(TestDbContext context)
    {
        var order = CreateOrder("ORD-SEED-001");
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }

    #endregion
}
