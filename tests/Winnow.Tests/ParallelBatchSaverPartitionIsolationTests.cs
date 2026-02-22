using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Shouldly;

namespace Winnow.Tests;

public class ParallelBatchSaverPartitionIsolationTests : ParallelTestBase
{
    [Fact]
    public async Task FailureInOnePartition_DoesNotAffectOtherPartitions()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.OrderBy(p => p.Id).ToList());

        // Make first product invalid (will be in first partition)
        products[0].Price = -10;
        // All others valid
        foreach (var p in products.Skip(1)) p.Price += 5;

        var result = await saver.UpdateBatchAsync(products);

        result.SuccessCount.ShouldBeGreaterThan(0);
        result.FailureCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ValidationError_InOneEntity_OthersInDifferentPartitionSucceed()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.OrderBy(p => p.Id).ToList());

        // Invalid product in one partition
        products[0].Price = -10;
        // Valid products in other partition
        products[2].Price += 5;
        products[3].Price += 5;

        var result = await saver.UpdateBatchAsync(products);

        // At least the valid partition should succeed
        result.SuccessCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task EachPartition_OperatesOnIndependentEntities()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = new TestDataBuilder().CreateValidProducts(6);
        foreach (var p in products) p.Id = 0;

        var result = await saver.InsertBatchAsync(products);

        result.IsCompleteSuccess.ShouldBeTrue();

        // All 6 should be in the database with unique IDs
        var dbProducts = QueryWithFactory(ctx => ctx.Products.ToList());
        dbProducts.Count.ShouldBe(6);
        dbProducts.Select(p => p.Id).Distinct().Count().ShouldBe(6);
    }

    [Fact]
    public async Task FailedPartition_EntitiesAppearInFailures()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.OrderBy(p => p.Id).ToList());

        // Make first product invalid
        products[0].Price = -10;
        foreach (var p in products.Skip(1)) p.Price += 5;

        var result = await saver.UpdateBatchAsync(products);

        result.Failures.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task SucceededPartition_EntitiesAppearInSuccessfulIds()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.OrderBy(p => p.Id).ToList());

        // Make first product invalid
        products[0].Price = -10;
        foreach (var p in products.Skip(1)) p.Price += 5;

        var result = await saver.UpdateBatchAsync(products);

        result.SuccessfulIds.Count.ShouldBeGreaterThan(0);
    }
}
