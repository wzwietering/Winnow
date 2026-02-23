using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

public class ParallelWinnowerBackwardsCompatTests : ParallelTestBase
{
    [Fact]
    public async Task Update_Sequential_MatchesWinnower()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        var parallelSaver = CreateSaver(maxDegreeOfParallelism: 1);
        var parallelResult = await parallelSaver.UpdateAsync(products);

        // Re-seed for Winnower comparison
        ResetDatabase();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        using var context = (TestDbContext)CreateContextFactory()();
        var batchProducts = context.Products.ToList();
        foreach (var p in batchProducts) p.Price += 5;

        var winnower = new Winnower<Product, int>(context);
        var winnowResult = await winnower.UpdateAsync(batchProducts);

        parallelResult.SuccessCount.ShouldBe(winnowResult.SuccessCount);
        parallelResult.FailureCount.ShouldBe(winnowResult.FailureCount);
        parallelResult.SuccessfulIds.OrderBy(id => id).ToList()
            .ShouldBe(winnowResult.SuccessfulIds.OrderBy(id => id).ToList());
    }

    [Fact]
    public async Task Insert_Sequential_MatchesWinnower()
    {
        EnsureDatabaseCreated();

        var products1 = new TestDataBuilder().CreateValidProducts(6);
        foreach (var p in products1) p.Id = 0;

        var parallelSaver = CreateSaver(maxDegreeOfParallelism: 1);
        var parallelResult = await parallelSaver.InsertAsync(products1);

        // Re-seed for Winnower comparison
        ResetDatabase();

        using var context = (TestDbContext)CreateContextFactory()();
        var products2 = new TestDataBuilder().CreateValidProducts(6);
        foreach (var p in products2) p.Id = 0;

        var winnower = new Winnower<Product, int>(context);
        var winnowResult = await winnower.InsertAsync(products2);

        parallelResult.SuccessCount.ShouldBe(winnowResult.SuccessCount);
        parallelResult.InsertedEntities.Count.ShouldBe(winnowResult.InsertedEntities.Count);

        var parallelIndices = parallelResult.InsertedEntities.Select(e => e.OriginalIndex).OrderBy(i => i).ToList();
        var winnowerIndices = winnowResult.InsertedEntities.Select(e => e.OriginalIndex).OrderBy(i => i).ToList();
        parallelIndices.ShouldBe(winnowerIndices);
    }

    [Fact]
    public async Task Delete_Sequential_MatchesWinnower()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        var parallelSaver = CreateSaver(maxDegreeOfParallelism: 1);
        var parallelResult = await parallelSaver.DeleteAsync(products);

        // Re-seed for Winnower comparison
        ResetDatabase();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        using var context = (TestDbContext)CreateContextFactory()();
        var batchProducts = context.Products.ToList();
        var winnower = new Winnower<Product, int>(context);
        var winnowResult = await winnower.DeleteAsync(batchProducts);

        parallelResult.SuccessCount.ShouldBe(winnowResult.SuccessCount);
        parallelResult.SuccessfulIds.OrderBy(id => id).ToList()
            .ShouldBe(winnowResult.SuccessfulIds.OrderBy(id => id).ToList());
    }

    [Fact]
    public async Task Upsert_Sequential_MatchesWinnower()
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
        var parallelResult = await parallelSaver.UpsertAsync(all1);

        // Re-seed for Winnower comparison
        ResetDatabase();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        using var context = (TestDbContext)CreateContextFactory()();
        var existing2 = context.Products.Take(2).ToList();
        foreach (var p in existing2) p.Price += 10;
        var newProducts2 = new TestDataBuilder().CreateValidProducts(2);
        foreach (var p in newProducts2) p.Id = 0;
        var all2 = existing2.Concat(newProducts2).ToList();

        var winnower = new Winnower<Product, int>(context);
        var winnowResult = await winnower.UpsertAsync(all2);

        parallelResult.SuccessCount.ShouldBe(winnowResult.SuccessCount);
        parallelResult.InsertedEntities.Count.ShouldBe(winnowResult.InsertedEntities.Count);
        parallelResult.UpdatedEntities.Count.ShouldBe(winnowResult.UpdatedEntities.Count);
    }

    [Fact]
    public async Task UpdateGraph_Sequential_MatchesWinnower()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 4, itemsPerOrder: 2));

        var factory = CreateContextFactory();
        var orders1 = QueryWithFactory(ctx =>
            ctx.CustomerOrders.Include(o => o.OrderItems).ToList());
        foreach (var o in orders1) o.Status = CustomerOrderStatus.Processing;

        var parallelSaver = new ParallelWinnower<CustomerOrder, int>(factory, maxDegreeOfParallelism: 1);
        var parallelResult = await parallelSaver.UpdateGraphAsync(orders1);

        // Re-seed for Winnower comparison
        ResetDatabase();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 4, itemsPerOrder: 2));

        using var context = (TestDbContext)CreateContextFactory()();
        var orders2 = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        foreach (var o in orders2) o.Status = CustomerOrderStatus.Processing;

        var winnower = new Winnower<CustomerOrder, int>(context);
        var winnowResult = await winnower.UpdateGraphAsync(orders2);

        parallelResult.SuccessCount.ShouldBe(winnowResult.SuccessCount);
        parallelResult.GraphHierarchy.ShouldNotBeNull();
        winnowResult.GraphHierarchy.ShouldNotBeNull();
    }

    [Fact]
    public async Task InsertGraph_Sequential_MatchesWinnower()
    {
        EnsureDatabaseCreated();

        var factory = CreateContextFactory();
        var orders1 = CreateOrders(4, 2);

        var parallelSaver = new ParallelWinnower<CustomerOrder, int>(factory, maxDegreeOfParallelism: 1);
        var parallelResult = await parallelSaver.InsertGraphAsync(orders1);

        // Re-seed for Winnower comparison
        ResetDatabase();

        using var context = (TestDbContext)CreateContextFactory()();
        var orders2 = CreateOrders(4, 2);

        var winnower = new Winnower<CustomerOrder, int>(context);
        var winnowResult = await winnower.InsertGraphAsync(orders2);

        parallelResult.SuccessCount.ShouldBe(winnowResult.SuccessCount);
        parallelResult.InsertedEntities.Count.ShouldBe(winnowResult.InsertedEntities.Count);
    }

    [Fact]
    public async Task DeleteGraph_Sequential_MatchesWinnower()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 4, itemsPerOrder: 2));

        var factory = CreateContextFactory();
        var orders1 = QueryWithFactory(ctx =>
            ctx.CustomerOrders.Include(o => o.OrderItems).ToList());

        var parallelSaver = new ParallelWinnower<CustomerOrder, int>(factory, maxDegreeOfParallelism: 1);
        var parallelResult = await parallelSaver.DeleteGraphAsync(orders1);

        // Re-seed for Winnower comparison
        ResetDatabase();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 4, itemsPerOrder: 2));

        using var context = (TestDbContext)CreateContextFactory()();
        var orders2 = context.CustomerOrders.Include(o => o.OrderItems).ToList();

        var winnower = new Winnower<CustomerOrder, int>(context);
        var winnowResult = await winnower.DeleteGraphAsync(orders2);

        parallelResult.SuccessCount.ShouldBe(winnowResult.SuccessCount);
    }

    [Fact]
    public async Task UpsertGraph_Sequential_MatchesWinnower()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 2, itemsPerOrder: 2));

        var factory = CreateContextFactory();
        var existing1 = QueryWithFactory(ctx =>
            ctx.CustomerOrders.Include(o => o.OrderItems).ToList());
        foreach (var o in existing1) o.Status = CustomerOrderStatus.Completed;

        var parallelSaver = new ParallelWinnower<CustomerOrder, int>(factory, maxDegreeOfParallelism: 1);
        var parallelResult = await parallelSaver.UpsertGraphAsync(existing1);

        // Re-seed for Winnower comparison
        ResetDatabase();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 2, itemsPerOrder: 2));

        using var context = (TestDbContext)CreateContextFactory()();
        var existing2 = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        foreach (var o in existing2) o.Status = CustomerOrderStatus.Completed;

        var winnower = new Winnower<CustomerOrder, int>(context);
        var winnowResult = await winnower.UpsertGraphAsync(existing2);

        parallelResult.SuccessCount.ShouldBe(winnowResult.SuccessCount);
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
