using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

public class ParallelBatchSaverBackwardsCompatTests : ParallelTestBase
{
    [Fact]
    public async Task Update_Sequential_MatchesBatchSaver()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        var parallelSaver = CreateSaver(maxDegreeOfParallelism: 1);
        var parallelResult = await parallelSaver.UpdateBatchAsync(products);

        // Re-seed for BatchSaver comparison
        ResetDatabase();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        using var context = (TestDbContext)CreateContextFactory()();
        var batchProducts = context.Products.ToList();
        foreach (var p in batchProducts) p.Price += 5;

        var batchSaver = new BatchSaver<Product, int>(context);
        var batchResult = await batchSaver.UpdateBatchAsync(batchProducts);

        parallelResult.SuccessCount.ShouldBe(batchResult.SuccessCount);
        parallelResult.FailureCount.ShouldBe(batchResult.FailureCount);
        parallelResult.SuccessfulIds.OrderBy(id => id).ToList()
            .ShouldBe(batchResult.SuccessfulIds.OrderBy(id => id).ToList());
    }

    [Fact]
    public async Task Insert_Sequential_MatchesBatchSaver()
    {
        EnsureDatabaseCreated();

        var products1 = new TestDataBuilder().CreateValidProducts(6);
        foreach (var p in products1) p.Id = 0;

        var parallelSaver = CreateSaver(maxDegreeOfParallelism: 1);
        var parallelResult = await parallelSaver.InsertBatchAsync(products1);

        // Re-seed for BatchSaver comparison
        ResetDatabase();

        using var context = (TestDbContext)CreateContextFactory()();
        var products2 = new TestDataBuilder().CreateValidProducts(6);
        foreach (var p in products2) p.Id = 0;

        var batchSaver = new BatchSaver<Product, int>(context);
        var batchResult = await batchSaver.InsertBatchAsync(products2);

        parallelResult.SuccessCount.ShouldBe(batchResult.SuccessCount);
        parallelResult.InsertedEntities.Count.ShouldBe(batchResult.InsertedEntities.Count);

        var parallelIndices = parallelResult.InsertedEntities.Select(e => e.OriginalIndex).OrderBy(i => i).ToList();
        var batchIndices = batchResult.InsertedEntities.Select(e => e.OriginalIndex).OrderBy(i => i).ToList();
        parallelIndices.ShouldBe(batchIndices);
    }

    [Fact]
    public async Task Delete_Sequential_MatchesBatchSaver()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        var parallelSaver = CreateSaver(maxDegreeOfParallelism: 1);
        var parallelResult = await parallelSaver.DeleteBatchAsync(products);

        // Re-seed for BatchSaver comparison
        ResetDatabase();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        using var context = (TestDbContext)CreateContextFactory()();
        var batchProducts = context.Products.ToList();
        var batchSaver = new BatchSaver<Product, int>(context);
        var batchResult = await batchSaver.DeleteBatchAsync(batchProducts);

        parallelResult.SuccessCount.ShouldBe(batchResult.SuccessCount);
        parallelResult.SuccessfulIds.OrderBy(id => id).ToList()
            .ShouldBe(batchResult.SuccessfulIds.OrderBy(id => id).ToList());
    }

    [Fact]
    public async Task Upsert_Sequential_MatchesBatchSaver()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        // Mix: 2 existing (update) + 2 new (insert)
        var existing1 = QueryWithFactory(ctx => ctx.Products.Take(2).ToList());
        foreach (var p in existing1) p.Price += 10;
        var newProducts1 = new TestDataBuilder().CreateValidProducts(2);
        foreach (var p in newProducts1) p.Id = 0;
        var all1 = existing1.Concat(newProducts1).ToList();

        var parallelSaver = CreateSaver(maxDegreeOfParallelism: 1);
        var parallelResult = await parallelSaver.UpsertBatchAsync(all1);

        // Re-seed for BatchSaver comparison
        ResetDatabase();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        using var context = (TestDbContext)CreateContextFactory()();
        var existing2 = context.Products.Take(2).ToList();
        foreach (var p in existing2) p.Price += 10;
        var newProducts2 = new TestDataBuilder().CreateValidProducts(2);
        foreach (var p in newProducts2) p.Id = 0;
        var all2 = existing2.Concat(newProducts2).ToList();

        var batchSaver = new BatchSaver<Product, int>(context);
        var batchResult = await batchSaver.UpsertBatchAsync(all2);

        parallelResult.SuccessCount.ShouldBe(batchResult.SuccessCount);
        parallelResult.InsertedEntities.Count.ShouldBe(batchResult.InsertedEntities.Count);
        parallelResult.UpdatedEntities.Count.ShouldBe(batchResult.UpdatedEntities.Count);
    }

    [Fact]
    public async Task UpdateGraph_Sequential_MatchesBatchSaver()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 4, itemsPerOrder: 2));

        var factory = CreateContextFactory();
        var orders1 = QueryWithFactory(ctx =>
            ctx.CustomerOrders.Include(o => o.OrderItems).ToList());
        foreach (var o in orders1) o.Status = CustomerOrderStatus.Processing;

        var parallelSaver = new ParallelBatchSaver<CustomerOrder, int>(factory, maxDegreeOfParallelism: 1);
        var parallelResult = await parallelSaver.UpdateGraphBatchAsync(orders1);

        // Re-seed for BatchSaver comparison
        ResetDatabase();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 4, itemsPerOrder: 2));

        using var context = (TestDbContext)CreateContextFactory()();
        var orders2 = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        foreach (var o in orders2) o.Status = CustomerOrderStatus.Processing;

        var batchSaver = new BatchSaver<CustomerOrder, int>(context);
        var batchResult = await batchSaver.UpdateGraphBatchAsync(orders2);

        parallelResult.SuccessCount.ShouldBe(batchResult.SuccessCount);
        parallelResult.GraphHierarchy.ShouldNotBeNull();
        batchResult.GraphHierarchy.ShouldNotBeNull();
    }

    [Fact]
    public async Task InsertGraph_Sequential_MatchesBatchSaver()
    {
        EnsureDatabaseCreated();

        var factory = CreateContextFactory();
        var orders1 = CreateOrders(4, 2);

        var parallelSaver = new ParallelBatchSaver<CustomerOrder, int>(factory, maxDegreeOfParallelism: 1);
        var parallelResult = await parallelSaver.InsertGraphBatchAsync(orders1);

        // Re-seed for BatchSaver comparison
        ResetDatabase();

        using var context = (TestDbContext)CreateContextFactory()();
        var orders2 = CreateOrders(4, 2);

        var batchSaver = new BatchSaver<CustomerOrder, int>(context);
        var batchResult = await batchSaver.InsertGraphBatchAsync(orders2);

        parallelResult.SuccessCount.ShouldBe(batchResult.SuccessCount);
        parallelResult.InsertedEntities.Count.ShouldBe(batchResult.InsertedEntities.Count);
    }

    [Fact]
    public async Task DeleteGraph_Sequential_MatchesBatchSaver()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 4, itemsPerOrder: 2));

        var factory = CreateContextFactory();
        var orders1 = QueryWithFactory(ctx =>
            ctx.CustomerOrders.Include(o => o.OrderItems).ToList());

        var parallelSaver = new ParallelBatchSaver<CustomerOrder, int>(factory, maxDegreeOfParallelism: 1);
        var parallelResult = await parallelSaver.DeleteGraphBatchAsync(orders1);

        // Re-seed for BatchSaver comparison
        ResetDatabase();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 4, itemsPerOrder: 2));

        using var context = (TestDbContext)CreateContextFactory()();
        var orders2 = context.CustomerOrders.Include(o => o.OrderItems).ToList();

        var batchSaver = new BatchSaver<CustomerOrder, int>(context);
        var batchResult = await batchSaver.DeleteGraphBatchAsync(orders2);

        parallelResult.SuccessCount.ShouldBe(batchResult.SuccessCount);
    }

    [Fact]
    public async Task UpsertGraph_Sequential_MatchesBatchSaver()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 2, itemsPerOrder: 2));

        var factory = CreateContextFactory();
        var existing1 = QueryWithFactory(ctx =>
            ctx.CustomerOrders.Include(o => o.OrderItems).ToList());
        foreach (var o in existing1) o.Status = CustomerOrderStatus.Completed;

        var parallelSaver = new ParallelBatchSaver<CustomerOrder, int>(factory, maxDegreeOfParallelism: 1);
        var parallelResult = await parallelSaver.UpsertGraphBatchAsync(existing1);

        // Re-seed for BatchSaver comparison
        ResetDatabase();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 2, itemsPerOrder: 2));

        using var context = (TestDbContext)CreateContextFactory()();
        var existing2 = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        foreach (var o in existing2) o.Status = CustomerOrderStatus.Completed;

        var batchSaver = new BatchSaver<CustomerOrder, int>(context);
        var batchResult = await batchSaver.UpsertGraphBatchAsync(existing2);

        parallelResult.SuccessCount.ShouldBe(batchResult.SuccessCount);
    }

    private static List<CustomerOrder> CreateOrders(int count, int itemsPerOrder)
    {
        return Enumerable.Range(1, count).Select(i => new CustomerOrder
        {
            OrderNumber = $"ORD-BC-{i:D6}",
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
}
