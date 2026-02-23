using Winnow.Tests.CompositeKeyIntegration;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

public class SelectiveNavigationFilteringTests : TestBase
{
    // ========== InsertGraph Tests ==========

    [Fact]
    public void InsertGraph_IncludeOnlyOrderItems_ReservationsNotInserted()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-001", 2, 2);
        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
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

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
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

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order]);

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

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            NavigationFilter = filter
        });

        result.GraphHierarchy.ShouldNotBeNull();
        var rootNode = result.GraphHierarchy!.First();
        rootNode.Children.Count.ShouldBe(2);

        // No grandchildren in hierarchy (reservations filtered)
        rootNode.Children.ShouldAllBe(c => c.Children.Count == 0);
    }

    // ========== UpdateGraph Tests ==========

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

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpdateGraph(orders, new GraphOptions
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

        var saver = new Winnower<CustomerOrder, int>(context);

        // Should NOT throw orphan detection for filtered-out reservations
        var result = saver.UpdateGraph(orders, new GraphOptions
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

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpdateGraph(orders, new GraphOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Delete,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.OrderItems.Find(removedItemId).ShouldBeNull();
    }

    // ========== DeleteGraph Tests ==========

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

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.DeleteGraph(orders, new DeleteGraphOptions
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

    // ========== UpsertGraph Tests ==========

    [Fact]
    public void UpsertGraph_IncludeOrderItems_UpsertsOnlyItems()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-001", 2, 2);

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpsertGraph([order], new UpsertGraphOptions
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

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
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

        var saver = new Winnower<CustomerOrder, int>(context);
        var ex = Should.Throw<InvalidOperationException>(() =>
            saver.InsertGraph([order], new InsertGraphOptions
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

        var saver = new Winnower<Student, int>(context);
        var ex = Should.Throw<InvalidOperationException>(() =>
            saver.InsertGraph([student], new InsertGraphOptions
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

        var saver = new Winnower<Category, int>(context);
        var result = saver.InsertGraph([parent], new InsertGraphOptions
        {
            NavigationFilter = filter,
            CircularReferenceHandling = CircularReferenceHandling.Ignore
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        parent.Id.ShouldBeGreaterThan(0);
        parent.SubCategories.ShouldAllBe(c => c.Id > 0);
    }

    // ========== Many-to-Many with Filter Tests ==========

    [Fact]
    public void InsertGraph_SkipNavWithIncludeFilter_JoinRecordsCreated()
    {
        using var context = CreateContext();

        var courses = new List<Course>
        {
            new() { Code = "CS101", Title = "Intro CS", Credits = 3 },
            new() { Code = "CS102", Title = "Data Structures", Credits = 3 }
        };
        var student = new Student { Name = "Alice", Email = "alice@test.com", Courses = courses };

        var filter = NavigationFilter.Include()
            .Navigation<Student>(s => s.Courses);

        var saver = new Winnower<Student, int>(context);
        var result = saver.InsertGraph([student], new InsertGraphOptions
        {
            IncludeManyToMany = true,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.InsertIfNew,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        student.Id.ShouldBeGreaterThan(0);

        context.ChangeTracker.Clear();
        var loaded = context.Students.Include(s => s.Courses).First();
        loaded.Courses.Count.ShouldBe(2);
    }

    [Fact]
    public void InsertGraph_ExcludeFilterOnManyToMany_NoJoinRecords()
    {
        using var context = CreateContext();

        var courses = new List<Course>
        {
            new() { Code = "CS101", Title = "Intro CS", Credits = 3 }
        };
        var student = new Student { Name = "Bob", Email = "bob@test.com", Courses = courses };

        var filter = NavigationFilter.Exclude()
            .Navigation<Student>(s => s.Courses);

        var saver = new Winnower<Student, int>(context);
        var result = saver.InsertGraph([student], new InsertGraphOptions
        {
            IncludeManyToMany = true,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.InsertIfNew,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        student.Id.ShouldBeGreaterThan(0);

        context.ChangeTracker.Clear();
        var loaded = context.Students.Include(s => s.Courses).First();
        loaded.Courses.Count.ShouldBe(0);
    }

    [Fact]
    public void InsertGraph_ExplicitJoinWithFilter_EnrollmentsInserted()
    {
        using var context = CreateContext();

        var course = new Course { Code = "CS101", Title = "Intro CS", Credits = 3 };
        context.Courses.Add(course);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var student = new Student
        {
            Name = "Charlie",
            Email = "charlie@test.com",
            Enrollments = [new Enrollment { CourseId = course.Id, EnrolledAt = DateTime.UtcNow }]
        };

        var filter = NavigationFilter.Include()
            .Navigation<Student>(s => s.Enrollments);

        var saver = new Winnower<Student, int>(context);
        var result = saver.InsertGraph([student], new InsertGraphOptions
        {
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var loaded = context.Students.Include(s => s.Enrollments).First();
        loaded.Enrollments.Count.ShouldBe(1);
    }

    [Fact]
    public void InsertGraph_FilterWithManyToManyValidation_ValidatesIncludedNavs()
    {
        using var context = CreateContext();

        var courses = new List<Course>
        {
            new() { Code = "CS301", Title = "Algorithms", Credits = 3 }
        };
        var student = new Student { Name = "Diana", Email = "diana@test.com", Courses = courses };

        var filter = NavigationFilter.Include()
            .Navigation<Student>(s => s.Courses);

        var saver = new Winnower<Student, int>(context);
        var result = saver.InsertGraph([student], new InsertGraphOptions
        {
            IncludeManyToMany = true,
            ValidateManyToManyEntitiesExist = false,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.InsertIfNew,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var loaded = context.Students.Include(s => s.Courses).First();
        loaded.Courses.Count.ShouldBe(1);
    }

    [Fact]
    public void InsertGraph_FilterWithInsertIfNew_BehaviorRespected()
    {
        using var context = CreateContext();

        var newCourse = new Course { Code = "CS401", Title = "ML", Credits = 3 };
        var student = new Student { Name = "Eve", Email = "eve@test.com", Courses = [newCourse] };

        var filter = NavigationFilter.Include()
            .Navigation<Student>(s => s.Courses);

        var saver = new Winnower<Student, int>(context);
        var result = saver.InsertGraph([student], new InsertGraphOptions
        {
            IncludeManyToMany = true,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.InsertIfNew,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        newCourse.Id.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void UpsertGraph_ManyToManyWithFilter_JoinRecordsManaged()
    {
        using var context = CreateContext();

        var courses = new List<Course>
        {
            new() { Code = "CS501", Title = "Databases", Credits = 3 }
        };
        var student = new Student { Name = "Frank", Email = "frank@test.com", Courses = courses };

        var filter = NavigationFilter.Include()
            .Navigation<Student>(s => s.Courses);

        var saver = new Winnower<Student, int>(context);
        var result = saver.UpsertGraph([student], new UpsertGraphOptions
        {
            IncludeManyToMany = true,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.InsertIfNew,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        student.Id.ShouldBeGreaterThan(0);

        context.ChangeTracker.Clear();
        var loaded = context.Students.Include(s => s.Courses).First();
        loaded.Courses.Count.ShouldBe(1);
    }

    // ========== Reference Navigation with Filter Tests ==========

    [Fact]
    public void InsertGraph_FilterIncludesReferenceNav_ProductTraversed()
    {
        using var context = CreateContext();

        var product = new Product
        {
            Name = "Widget", Price = 9.99m, Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        };
        var order = new CustomerOrder
        {
            OrderNumber = "ORD-REF-001", CustomerName = "Test", CustomerId = 1,
            Status = CustomerOrderStatus.Pending, TotalAmount = 9.99m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductName = "Widget", Quantity = 1, UnitPrice = 9.99m,
                    Subtotal = 9.99m, Product = product
                }
            ]
        };

        var filter = NavigationFilter.Include()
            .Navigations<CustomerOrder>(o => o.OrderItems)
            .Navigation<OrderItem>(i => i.Product);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        product.Id.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void InsertGraph_FilterExcludesReferenceNav_ProductNotTraversed()
    {
        using var context = CreateContext();

        var product = new Product
        {
            Name = "Gadget", Price = 19.99m, Stock = 50,
            LastModified = DateTimeOffset.UtcNow
        };
        var order = new CustomerOrder
        {
            OrderNumber = "ORD-REF-002", CustomerName = "Test", CustomerId = 1,
            Status = CustomerOrderStatus.Pending, TotalAmount = 19.99m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductName = "Gadget", Quantity = 1, UnitPrice = 19.99m,
                    Subtotal = 19.99m, Product = product
                }
            ]
        };

        var filter = NavigationFilter.Exclude()
            .Navigation<OrderItem>(i => i.Product);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        product.Id.ShouldBe(0);
    }

    [Fact]
    public void InsertGraph_ExcludeModeReferenceNav_ProductBlocked()
    {
        using var context = CreateContext();

        var product = new Product
        {
            Name = "Blocked", Price = 5.00m, Stock = 10,
            LastModified = DateTimeOffset.UtcNow
        };
        var order = new CustomerOrder
        {
            OrderNumber = "ORD-REF-003", CustomerName = "Test", CustomerId = 1,
            Status = CustomerOrderStatus.Pending, TotalAmount = 5.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductName = "Blocked", Quantity = 1, UnitPrice = 5.00m,
                    Subtotal = 5.00m, Product = product
                }
            ]
        };

        var filter = NavigationFilter.Exclude()
            .Navigation<OrderItem>(i => i.Product);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        product.Id.ShouldBe(0);
    }

    [Fact]
    public void UpdateGraph_ReferenceNavWithCircularRef_FilterAndHandling()
    {
        using var context = CreateContext();

        var parent = new Category { Name = "Parent" };
        context.Categories.Add(parent);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loaded = context.Categories.First(c => c.Name == "Parent");
        loaded.Description = "Updated";

        var filter = NavigationFilter.Include()
            .Navigation<Category>(c => c.SubCategories);

        var saver = new Winnower<Category, int>(context);
        var result = saver.UpdateGraph([loaded], new GraphOptions
        {
            CircularReferenceHandling = CircularReferenceHandling.Ignore,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var verify = context.Categories.First(c => c.Id == loaded.Id);
        verify.Description.ShouldBe("Updated");
    }

    // ========== Validation Tests ==========

    [Fact]
    public void InsertGraph_IncludeFilterWithReferenceNavigation_Succeeds()
    {
        using var context = CreateContext();

        var order = CreateThreeLevelOrder("ORD-001", 1, 0);

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems)
            .Navigation<OrderItem>(i => i.Product);

        var saver = new Winnower<CustomerOrder, int>(context);

        // Include filter with a reference navigation works when IncludeReferences is true
        var result = saver.InsertGraph([order], new InsertGraphOptions
        {
            IncludeReferences = true,
            CircularReferenceHandling = CircularReferenceHandling.Ignore,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
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

// ========== Parallel Tests ==========

public class SelectiveNavigationFilteringParallelTests : ParallelTestBase
{
    [Fact]
    public async Task InsertGraphAsync_ParallelWithFilter_FilterAppliedAcrossPartitions()
    {
        EnsureDatabaseCreated();

        var factory = CreateContextFactory();
        var saver = new ParallelWinnower<CustomerOrder, int>(factory, maxDegreeOfParallelism: 2);

        var orders = Enumerable.Range(1, 10).Select(i => new CustomerOrder
        {
            OrderNumber = $"ORD-PF-{i:D3}",
            CustomerId = i, CustomerName = $"Customer {i}",
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
        }).ToList();

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var result = await saver.InsertGraphAsync(orders, new InsertGraphOptions
        {
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(10);

        var dbReservations = QueryWithFactory(ctx => ctx.ItemReservations.ToList());
        dbReservations.Count.ShouldBe(0);
    }

    [Fact]
    public async Task DeleteGraphAsync_ParallelWithFilter_CascadeRespected()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 4, itemsPerOrder: 2));

        var factory = CreateContextFactory();
        var saver = new ParallelWinnower<CustomerOrder, int>(factory, maxDegreeOfParallelism: 2);

        var orders = QueryWithFactory(ctx =>
            ctx.CustomerOrders.Include(o => o.OrderItems).ToList());

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var result = await saver.DeleteGraphAsync(orders, new DeleteGraphOptions
        {
            CascadeBehavior = DeleteCascadeBehavior.Cascade,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        var remaining = QueryWithFactory(ctx => ctx.CustomerOrders.ToList());
        remaining.Count.ShouldBe(0);
    }

    [Fact]
    public async Task UpsertGraphAsync_ParallelWithFilter_InsertsAndUpdates()
    {
        EnsureDatabaseCreated();

        var factory = CreateContextFactory();
        var saver = new ParallelWinnower<CustomerOrder, int>(factory, maxDegreeOfParallelism: 2);

        var orders = Enumerable.Range(1, 4).Select(i => new CustomerOrder
        {
            OrderNumber = $"ORD-UP-{i:D3}",
            CustomerId = i, CustomerName = $"Customer {i}",
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
        }).ToList();

        var filter = NavigationFilter.Include()
            .Navigation<CustomerOrder>(o => o.OrderItems);

        var result = await saver.UpsertGraphAsync(orders, new UpsertGraphOptions
        {
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        var dbReservations = QueryWithFactory(ctx => ctx.ItemReservations.ToList());
        dbReservations.Count.ShouldBe(0);
    }

    [Fact]
    public async Task InsertGraphAsync_ParallelExcludeFilter_FilterApplied()
    {
        EnsureDatabaseCreated();

        var factory = CreateContextFactory();
        var saver = new ParallelWinnower<CustomerOrder, int>(factory, maxDegreeOfParallelism: 2);

        var orders = Enumerable.Range(1, 6).Select(i => new CustomerOrder
        {
            OrderNumber = $"ORD-EX-{i:D3}",
            CustomerId = i, CustomerName = $"Customer {i}",
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
        }).ToList();

        var filter = NavigationFilter.Exclude()
            .Navigation<OrderItem>(i => i.Reservations);

        var result = await saver.InsertGraphAsync(orders, new InsertGraphOptions
        {
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        var dbItems = QueryWithFactory(ctx => ctx.OrderItems.ToList());
        dbItems.Count.ShouldBe(6);

        var dbReservations = QueryWithFactory(ctx => ctx.ItemReservations.ToList());
        dbReservations.Count.ShouldBe(0);
    }
}

// ========== Composite Key Tests ==========

public class SelectiveNavigationFilteringCompositeKeyTests : CompositeKeyTestBase
{
    [Fact]
    public void InsertGraph_CompositeKeyEntity_FilterExcludesNotes_OnlyOrderLineInserted()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLine = new OrderLine
        {
            OrderId = orderId, LineNumber = 1,
            Quantity = 5, UnitPrice = 10.00m,
            Notes = [new OrderLineNote { Note = "Test Note", CreatedAt = DateTime.UtcNow }]
        };

        var filter = NavigationFilter.Exclude()
            .Navigation<OrderLine>(ol => ol.Notes);

        var saver = new Winnower<OrderLine, CompositeKey>(context);
        var result = saver.InsertGraph([orderLine], new InsertGraphOptions
        {
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var notes = context.Set<OrderLineNote>().ToList();
        notes.Count.ShouldBe(0);
    }

    [Fact]
    public void UpdateGraph_CompositeKeyEntity_FilterExcludesNotes_NoOrphanDetection()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);
        InsertOrderLineWithNotes(context, orderId, 1, 2);

        var loaded = context.OrderLines
            .Include(ol => ol.Notes)
            .First(ol => ol.OrderId == orderId && ol.LineNumber == 1);
        loaded.Notes.Clear();

        var filter = NavigationFilter.Exclude()
            .Navigation<OrderLine>(ol => ol.Notes);

        var saver = new Winnower<OrderLine, CompositeKey>(context);
        var result = saver.UpdateGraph([loaded], new GraphOptions
        {
            OrphanedChildBehavior = OrphanBehavior.Throw,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public void DeleteGraph_CompositeKeyEntity_FilterIncludesNotes_CascadeDeletes()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);
        InsertOrderLineWithNotes(context, orderId, 1, 2);

        var loaded = context.OrderLines
            .Include(ol => ol.Notes)
            .First(ol => ol.OrderId == orderId && ol.LineNumber == 1);

        var filter = NavigationFilter.Include()
            .Navigation<OrderLine>(ol => ol.Notes);

        var saver = new Winnower<OrderLine, CompositeKey>(context);
        var result = saver.DeleteGraph([loaded], new DeleteGraphOptions
        {
            CascadeBehavior = DeleteCascadeBehavior.Cascade,
            NavigationFilter = filter
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var notes = context.Set<OrderLineNote>().ToList();
        notes.Count.ShouldBe(0);
    }
}
