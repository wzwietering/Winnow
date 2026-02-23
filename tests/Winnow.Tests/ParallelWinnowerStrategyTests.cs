using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

public class ParallelWinnowerStrategyTests : ParallelTestBase
{
    [Theory]
    [InlineData(BatchStrategy.OneByOne)]
    [InlineData(BatchStrategy.DivideAndConquer)]
    public async Task InsertAsync_AllInserted(BatchStrategy strategy)
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = new TestDataBuilder().CreateValidProducts(6);
        foreach (var p in products) p.Id = 0;

        var options = new InsertOptions { Strategy = strategy };
        var result = await saver.InsertAsync(products, options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(6);
    }

    [Theory]
    [InlineData(BatchStrategy.OneByOne)]
    [InlineData(BatchStrategy.DivideAndConquer)]
    public async Task UpdateAsync_AllUpdated(BatchStrategy strategy)
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        var options = new WinnowOptions { Strategy = strategy };
        var result = await saver.UpdateAsync(products, options);

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
        var result1 = await saver1.UpdateAsync(products1, new WinnowOptions { Strategy = BatchStrategy.OneByOne });

        // Re-seed for second run
        ResetDatabase();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var products2 = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products2) p.Price += 5;

        var saver2 = CreateSaver(maxDegreeOfParallelism: 2);
        var result2 = await saver2.UpdateAsync(products2, new WinnowOptions { Strategy = BatchStrategy.DivideAndConquer });

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
        var result1 = await saver1.UpdateAsync(products1, new WinnowOptions { Strategy = BatchStrategy.OneByOne });

        // Re-seed for second run
        ResetDatabase();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var products2 = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products2) p.Price += 5;

        var saver2 = CreateSaver(maxDegreeOfParallelism: 2);
        var result2 = await saver2.UpdateAsync(products2, new WinnowOptions { Strategy = BatchStrategy.DivideAndConquer });

        result2.DatabaseRoundTrips.ShouldBeLessThanOrEqualTo(result1.DatabaseRoundTrips);
    }

    [Theory]
    [InlineData(BatchStrategy.OneByOne)]
    [InlineData(BatchStrategy.DivideAndConquer)]
    public async Task InsertAsync_WithParallel_CorrectResults(BatchStrategy strategy)
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = new TestDataBuilder().CreateValidProducts(6);
        foreach (var p in products) p.Id = 0;

        var result = await saver.InsertAsync(products, new InsertOptions { Strategy = strategy });

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
        var saver = new ParallelWinnower<CustomerOrder, int>(factory, maxDegreeOfParallelism: 2);
        var orders = QueryWithFactory(ctx =>
            ctx.CustomerOrders.Include(o => o.OrderItems).ToList());
        foreach (var o in orders) o.Status = CustomerOrderStatus.Processing;

        var options = new GraphOptions { Strategy = BatchStrategy.OneByOne };
        var result = await saver.UpdateGraphAsync(orders, options);

        result.IsCompleteSuccess.ShouldBeTrue();
    }
}
