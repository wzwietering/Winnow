using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

public class ParallelBatchSaverTests : ParallelTestBase
{
    [Fact]
    public async Task UpdateBatchAsync_AllSucceed_ReturnsAllSuccessful()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        var result = await saver.UpdateBatchAsync(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(6);
        result.FailureCount.ShouldBe(0);
    }

    [Fact]
    public async Task UpdateBatchAsync_MixedSuccessFailure_ReportsCorrectly()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        products[0].Price = -10; // Invalid
        foreach (var p in products.Skip(1)) p.Price += 5;

        var result = await saver.UpdateBatchAsync(products);

        result.SuccessCount.ShouldBe(5);
        result.FailureCount.ShouldBe(1);
    }

    [Fact]
    public async Task InsertBatchAsync_AllSucceed_ReturnsAllInserted()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var builder = new TestDataBuilder();
        var products = builder.CreateValidProducts(6);
        // Reset IDs to 0 for insert
        foreach (var p in products) p.Id = 0;

        var result = await saver.InsertBatchAsync(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(6);
        result.InsertedEntities.Count.ShouldBe(6);
    }

    [Fact]
    public async Task InsertBatchAsync_OriginalIndex_PreservedAcrossPartitions()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var builder = new TestDataBuilder();
        var products = builder.CreateValidProducts(6);
        foreach (var p in products) p.Id = 0;

        var result = await saver.InsertBatchAsync(products);

        var indices = result.InsertedEntities.Select(e => e.OriginalIndex).OrderBy(i => i).ToList();
        indices.ShouldBe(Enumerable.Range(0, 6).ToList());
    }

    [Fact]
    public async Task DeleteBatchAsync_AllSucceed_ReturnsAllDeleted()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());

        var result = await saver.DeleteBatchAsync(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(6);

        var remaining = QueryWithFactory(ctx => ctx.Products.ToList());
        remaining.Count.ShouldBe(0);
    }

    [Fact]
    public async Task UpsertBatchAsync_AllSucceed_ReturnsAllUpserted()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        // Mix of updates (existing IDs) and inserts (ID = 0)
        var existing = QueryWithFactory(ctx => ctx.Products.Take(2).ToList());
        foreach (var p in existing) p.Price += 10;

        var newProducts = new TestDataBuilder().CreateValidProducts(2);
        foreach (var p in newProducts) p.Id = 0;

        var all = existing.Concat(newProducts).ToList();
        var result = await saver.UpsertBatchAsync(all);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
    }

    [Fact]
    public async Task UpsertBatchAsync_OriginalIndex_PreservedAcrossPartitions()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var builder = new TestDataBuilder();
        var products = builder.CreateValidProducts(6);
        foreach (var p in products) p.Id = 0;

        var result = await saver.UpsertBatchAsync(products);

        var indices = result.AllUpsertedEntities.Select(e => e.OriginalIndex).OrderBy(i => i).ToList();
        indices.ShouldBe(Enumerable.Range(0, 6).ToList());
    }

    [Fact]
    public async Task UpdateBatchAsync_EmptyCollection_ReturnsEmptyResult()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var result = await saver.UpdateBatchAsync(new List<Product>());

        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
    }

    [Fact]
    public async Task UpdateBatchAsync_SingleEntity_Works()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 1));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        products[0].Price += 5;

        var result = await saver.UpdateBatchAsync(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);
    }

    [Fact]
    public async Task UpdateBatchAsync_RoundTrips_SummedAcrossPartitions()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        var result = await saver.UpdateBatchAsync(products);

        result.DatabaseRoundTrips.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task UpdateBatchAsync_Duration_ReflectsWallClockTime()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        var result = await saver.UpdateBatchAsync(products);

        result.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    // === Sync method tests ===

    [Fact]
    public void UpdateBatch_Sync_AllSucceed()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        var result = saver.UpdateBatch(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
    }

    [Fact]
    public void InsertBatch_Sync_AllSucceed()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = new TestDataBuilder().CreateValidProducts(4);
        foreach (var p in products) p.Id = 0;

        var result = saver.InsertBatch(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
    }

    [Fact]
    public void DeleteBatch_Sync_AllSucceed()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());

        var result = saver.DeleteBatch(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
    }

    [Fact]
    public void UpsertBatch_Sync_AllSucceed()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = new TestDataBuilder().CreateValidProducts(4);
        foreach (var p in products) p.Id = 0;

        var result = saver.UpsertBatch(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
    }
}
