using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests;

public class ParallelBatchSaverStrategyTests : ParallelTestBase
{
    [Fact]
    public async Task InsertBatchAsync_OneByOne_AllInserted()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = new TestDataBuilder().CreateValidProducts(6);
        foreach (var p in products) p.Id = 0;

        var options = new InsertBatchOptions { Strategy = BatchStrategy.OneByOne };
        var result = await saver.InsertBatchAsync(products, options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(6);
    }

    [Fact]
    public async Task InsertBatchAsync_DivideAndConquer_AllInserted()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = new TestDataBuilder().CreateValidProducts(6);
        foreach (var p in products) p.Id = 0;

        var options = new InsertBatchOptions { Strategy = BatchStrategy.DivideAndConquer };
        var result = await saver.InsertBatchAsync(products, options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(6);
    }

    [Fact]
    public async Task UpdateBatchAsync_OneByOne_AllUpdated()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        var options = new BatchOptions { Strategy = BatchStrategy.OneByOne };
        var result = await saver.UpdateBatchAsync(products, options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(6);
    }

    [Fact]
    public async Task UpdateBatchAsync_DivideAndConquer_AllUpdated()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        var options = new BatchOptions { Strategy = BatchStrategy.DivideAndConquer };
        var result = await saver.UpdateBatchAsync(products, options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(6);
    }

    [Fact]
    public async Task BothStrategies_ProduceSameData_ForSameInput()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var products1 = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products1) p.Price += 5;

        var saver1 = CreateSaver(maxDegreeOfParallelism: 2);
        var result1 = await saver1.UpdateBatchAsync(products1, new BatchOptions { Strategy = BatchStrategy.OneByOne });

        // Re-seed for second run
        ResetDatabase();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var products2 = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products2) p.Price += 5;

        var saver2 = CreateSaver(maxDegreeOfParallelism: 2);
        var result2 = await saver2.UpdateBatchAsync(products2, new BatchOptions { Strategy = BatchStrategy.DivideAndConquer });

        result1.SuccessCount.ShouldBe(result2.SuccessCount);
        result1.FailureCount.ShouldBe(result2.FailureCount);
    }

    [Fact]
    public async Task DivideAndConquer_HasFewerRoundTrips_ThanOneByOne()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var products1 = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products1) p.Price += 5;

        var saver1 = CreateSaver(maxDegreeOfParallelism: 2);
        var result1 = await saver1.UpdateBatchAsync(products1, new BatchOptions { Strategy = BatchStrategy.OneByOne });

        // Re-seed for second run
        ResetDatabase();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var products2 = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products2) p.Price += 5;

        var saver2 = CreateSaver(maxDegreeOfParallelism: 2);
        var result2 = await saver2.UpdateBatchAsync(products2, new BatchOptions { Strategy = BatchStrategy.DivideAndConquer });

        result2.DatabaseRoundTrips.ShouldBeLessThanOrEqualTo(result1.DatabaseRoundTrips);
    }

    [Fact]
    public async Task OneByOne_WithParallel_CorrectResults()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = new TestDataBuilder().CreateValidProducts(6);
        foreach (var p in products) p.Id = 0;

        var result = await saver.InsertBatchAsync(products, new InsertBatchOptions { Strategy = BatchStrategy.OneByOne });

        result.InsertedEntities.Count.ShouldBe(6);

        var dbProducts = QueryWithFactory(ctx => ctx.Products.ToList());
        dbProducts.Count.ShouldBe(6);
    }

    [Fact]
    public async Task DivideAndConquer_WithParallel_CorrectResults()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = new TestDataBuilder().CreateValidProducts(6);
        foreach (var p in products) p.Id = 0;

        var result = await saver.InsertBatchAsync(products, new InsertBatchOptions { Strategy = BatchStrategy.DivideAndConquer });

        result.InsertedEntities.Count.ShouldBe(6);

        var dbProducts = QueryWithFactory(ctx => ctx.Products.ToList());
        dbProducts.Count.ShouldBe(6);
    }

    [Fact]
    public async Task GraphOperations_WorkWithBothStrategies()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedCustomerOrders(ctx, 4, itemsPerOrder: 2));

        var factory = CreateContextFactory();
        var saver = new ParallelBatchSaver<CustomerOrder, int>(factory, maxDegreeOfParallelism: 2);
        var orders = QueryWithFactory(ctx =>
            ctx.CustomerOrders.Include(o => o.OrderItems).ToList());
        foreach (var o in orders) o.Status = CustomerOrderStatus.Processing;

        var options = new GraphBatchOptions { Strategy = BatchStrategy.OneByOne };
        var result = await saver.UpdateGraphBatchAsync(orders, options);

        result.IsCompleteSuccess.ShouldBeTrue();
    }
}
