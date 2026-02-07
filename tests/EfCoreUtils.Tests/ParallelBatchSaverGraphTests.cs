using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests;

public class ParallelBatchSaverGraphTests : ParallelTestBase
{
    [Fact]
    public async Task UpdateGraphBatchAsync_ParentAndChildModified_BothUpdated()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 4, itemsPerOrder: 2));

        var factory = CreateContextFactory();
        var saver = new ParallelBatchSaver<CustomerOrder, int>(factory, maxDegreeOfParallelism: 2);
        var orders = QueryWithFactory(ctx =>
            ctx.CustomerOrders.Include(o => o.OrderItems).ToList());

        orders[0].Status = CustomerOrderStatus.Processing;
        var firstItem = orders[0].OrderItems.First();
        firstItem.Quantity = 999;
        firstItem.Subtotal = firstItem.Quantity * firstItem.UnitPrice;

        var result = await saver.UpdateGraphBatchAsync(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);

        var verifiedOrder = QuerySingleWithFactory(ctx =>
            ctx.CustomerOrders.Include(o => o.OrderItems).First(o => o.Id == orders[0].Id));
        verifiedOrder.Status.ShouldBe(CustomerOrderStatus.Processing);
        verifiedOrder.OrderItems.First(i => i.Id == firstItem.Id).Quantity.ShouldBe(999);
    }

    [Fact]
    public async Task InsertGraphBatchAsync_MultiLevelGraph_AllInserted()
    {
        EnsureDatabaseCreated();

        var factory = CreateContextFactory();
        var saver = new ParallelBatchSaver<CustomerOrder, int>(factory, maxDegreeOfParallelism: 2);

        var orders = CreateOrdersWithItems(4, 2);

        var result = await saver.InsertGraphBatchAsync(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);

        var dbOrders = QueryWithFactory(ctx =>
            ctx.CustomerOrders.Include(o => o.OrderItems).ToList());
        dbOrders.Count.ShouldBe(4);
        dbOrders.All(o => o.OrderItems.Count == 2).ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteGraphBatchAsync_CascadeDeletes_AllRemoved()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 4, itemsPerOrder: 2));

        var factory = CreateContextFactory();
        var saver = new ParallelBatchSaver<CustomerOrder, int>(factory, maxDegreeOfParallelism: 2);
        var orders = QueryWithFactory(ctx =>
            ctx.CustomerOrders.Include(o => o.OrderItems).ToList());

        var result = await saver.DeleteGraphBatchAsync(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);

        var remainingOrders = QueryWithFactory(ctx => ctx.CustomerOrders.ToList());
        var remainingItems = QueryWithFactory(ctx => ctx.OrderItems.ToList());
        remainingOrders.Count.ShouldBe(0);
        remainingItems.Count.ShouldBe(0);
    }

    [Fact]
    public async Task UpsertGraphBatchAsync_MixedInsertUpdate_Works()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 2, itemsPerOrder: 2));

        var factory = CreateContextFactory();
        var saver = new ParallelBatchSaver<CustomerOrder, int>(factory, maxDegreeOfParallelism: 2);

        // Existing orders to update
        var existing = QueryWithFactory(ctx =>
            ctx.CustomerOrders.Include(o => o.OrderItems).ToList());
        foreach (var o in existing) o.Status = CustomerOrderStatus.Completed;

        // New orders to insert
        var newOrders = CreateOrdersWithItems(2, 2, startId: 100);

        var all = existing.Concat(newOrders).ToList();
        var result = await saver.UpsertGraphBatchAsync(all);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
    }

    [Fact]
    public async Task UpdateGraphBatchAsync_GraphHierarchy_NotNull()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 4, itemsPerOrder: 2));

        var factory = CreateContextFactory();
        var saver = new ParallelBatchSaver<CustomerOrder, int>(factory, maxDegreeOfParallelism: 2);
        var orders = QueryWithFactory(ctx =>
            ctx.CustomerOrders.Include(o => o.OrderItems).ToList());
        foreach (var o in orders) o.Status = CustomerOrderStatus.Processing;

        var result = await saver.UpdateGraphBatchAsync(orders);

        result.GraphHierarchy.ShouldNotBeNull();
        result.GraphHierarchy.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task UpdateGraphBatchAsync_TraversalInfo_Aggregated()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 4, itemsPerOrder: 2));

        var factory = CreateContextFactory();
        var saver = new ParallelBatchSaver<CustomerOrder, int>(factory, maxDegreeOfParallelism: 2);
        var orders = QueryWithFactory(ctx =>
            ctx.CustomerOrders.Include(o => o.OrderItems).ToList());
        foreach (var o in orders) o.Status = CustomerOrderStatus.Processing;

        var result = await saver.UpdateGraphBatchAsync(orders);

        result.TraversalInfo.ShouldNotBeNull();
        result.TraversalInfo.TotalEntitiesTraversed.ShouldBeGreaterThan(0);
        result.TraversalInfo.MaxDepthReached.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task UpdateGraphBatchAsync_WithDetachOrphanBehavior_Succeeds()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 4, itemsPerOrder: 3));

        var factory = CreateContextFactory();
        var saver = new ParallelBatchSaver<CustomerOrder, int>(factory, maxDegreeOfParallelism: 2);
        var orders = QueryWithFactory(ctx =>
            ctx.CustomerOrders.Include(o => o.OrderItems).ToList());

        foreach (var o in orders) o.Status = CustomerOrderStatus.Processing;

        // Detach is the safest orphan behavior for parallel contexts
        var options = new GraphBatchOptions { OrphanedChildBehavior = OrphanBehavior.Detach };
        var result = await saver.UpdateGraphBatchAsync(orders, options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
    }

    [Fact]
    public async Task InsertGraphBatchAsync_WithReservations_ThreeLevelGraph()
    {
        EnsureDatabaseCreated();

        var factory = CreateContextFactory();
        var saver = new ParallelBatchSaver<CustomerOrder, int>(factory, maxDegreeOfParallelism: 2);

        var orders = CreateOrdersWithItemsAndReservations(4, 2, 1);

        var result = await saver.InsertGraphBatchAsync(orders);

        result.IsCompleteSuccess.ShouldBeTrue();

        var dbItems = QueryWithFactory(ctx =>
            ctx.OrderItems.Include(i => i.Reservations).ToList());
        dbItems.All(i => i.Reservations.Count >= 1).ShouldBeTrue();
    }

    [Fact]
    public async Task InsertGraphBatchAsync_ManyToMany_WorksWithSequentialPath()
    {
        EnsureDatabaseCreated();
        // Pre-seed courses that students will reference
        SeedWithFactory(ctx =>
        {
            ctx.Courses.AddRange(
                new Course { Id = 1, Code = "CS101", Title = "Intro to CS", Credits = 3 },
                new Course { Id = 2, Code = "CS201", Title = "Data Structures", Credits = 3 });
            ctx.SaveChanges();
        });

        // Many-to-many with existing entities requires a single context, use sequential path
        var factory = CreateContextFactory();
        var saver = new ParallelBatchSaver<Student, int>(factory, maxDegreeOfParallelism: 1);

        // Create fresh course references per student (not loaded from another context)
        var students = Enumerable.Range(0, 4).Select(i => new Student
        {
            Name = $"Student {i}",
            Email = $"student{i}@test.com",
            Courses = new List<Course>
            {
                new() { Id = 1, Code = "CS101", Title = "Intro to CS", Credits = 3 },
                new() { Id = 2, Code = "CS201", Title = "Data Structures", Credits = 3 }
            }
        }).ToList();

        var options = new InsertGraphBatchOptions { IncludeManyToMany = true };
        var result = await saver.InsertGraphBatchAsync(students, options);

        result.IsCompleteSuccess.ShouldBeTrue();

        var dbStudents = QueryWithFactory(ctx =>
            ctx.Students.Include(s => s.Courses).ToList());
        dbStudents.Count.ShouldBe(4);
        dbStudents.All(s => s.Courses.Count == 2).ShouldBeTrue();
    }

    [Fact]
    public async Task InsertBatchAsync_EmptyCollection_ReturnsEmptyResult()
    {
        EnsureDatabaseCreated();

        var factory = CreateContextFactory();
        var saver = new ParallelBatchSaver<CustomerOrder, int>(factory, maxDegreeOfParallelism: 2);

        var result = await saver.InsertGraphBatchAsync(new List<CustomerOrder>());

        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
    }

    private static List<CustomerOrder> CreateOrdersWithItems(int orderCount, int itemsPerOrder, int startId = 0)
    {
        return Enumerable.Range(1, orderCount).Select(i => new CustomerOrder
        {
            Id = startId > 0 ? 0 : i,
            OrderNumber = $"ORD-P-{startId + i:D6}",
            CustomerId = 1000 + i,
            CustomerName = $"Customer {i}",
            Status = CustomerOrderStatus.Pending,
            TotalAmount = itemsPerOrder * 22m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = Enumerable.Range(1, itemsPerOrder).Select(j => new OrderItem
            {
                ProductId = 1000 + j,
                ProductName = $"Product {j}",
                Quantity = 2,
                UnitPrice = 11m,
                Subtotal = 22m
            }).ToList<OrderItem>()
        }).ToList();
    }

    private static List<CustomerOrder> CreateOrdersWithItemsAndReservations(
        int orderCount, int itemsPerOrder, int reservationsPerItem)
    {
        return Enumerable.Range(1, orderCount).Select(i => new CustomerOrder
        {
            OrderNumber = $"ORD-R-{i:D6}",
            CustomerId = 1000 + i,
            CustomerName = $"Customer {i}",
            Status = CustomerOrderStatus.Pending,
            TotalAmount = itemsPerOrder * 22m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = Enumerable.Range(1, itemsPerOrder).Select(j => new OrderItem
            {
                ProductId = 1000 + j,
                ProductName = $"Product {j}",
                Quantity = 2,
                UnitPrice = 11m,
                Subtotal = 22m,
                Reservations = Enumerable.Range(1, reservationsPerItem).Select(k => new ItemReservation
                {
                    WarehouseLocation = $"WH-{k}",
                    ReservedQuantity = 1,
                    ReservedAt = DateTimeOffset.UtcNow
                }).ToList<ItemReservation>()
            }).ToList<OrderItem>()
        }).ToList();
    }
}
