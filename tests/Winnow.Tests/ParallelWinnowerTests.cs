using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

public class ParallelWinnowerTests : ParallelTestBase
{
    [Fact]
    public async Task UpdateAsync_AllSucceed_ReturnsAllSuccessful()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        var result = await saver.UpdateAsync(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(6);
        result.FailureCount.ShouldBe(0);
    }

    [Fact]
    public async Task UpdateAsync_MixedSuccessFailure_ReportsCorrectly()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        products[0].Price = -10; // Invalid
        foreach (var p in products.Skip(1)) p.Price += 5;

        var result = await saver.UpdateAsync(products);

        result.SuccessCount.ShouldBe(5);
        result.FailureCount.ShouldBe(1);
    }

    [Fact]
    public async Task InsertAsync_AllSucceed_ReturnsAllInserted()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var builder = new TestDataBuilder();
        var products = builder.CreateValidProducts(6);
        // Reset IDs to 0 for insert
        foreach (var p in products) p.Id = 0;

        var result = await saver.InsertAsync(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(6);
        result.InsertedEntities.Count.ShouldBe(6);
    }

    [Fact]
    public async Task InsertAsync_OriginalIndex_PreservedAcrossPartitions()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var builder = new TestDataBuilder();
        var products = builder.CreateValidProducts(6);
        foreach (var p in products) p.Id = 0;

        var result = await saver.InsertAsync(products);

        var indices = result.InsertedEntities.Select(e => e.OriginalIndex).OrderBy(i => i).ToList();
        indices.ShouldBe(Enumerable.Range(0, 6).ToList());
    }

    [Fact]
    public async Task DeleteAsync_AllSucceed_ReturnsAllDeleted()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());

        var result = await saver.DeleteAsync(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(6);

        var remaining = QueryWithFactory(ctx => ctx.Products.ToList());
        remaining.Count.ShouldBe(0);
    }

    [Fact]
    public async Task UpsertAsync_AllSucceed_ReturnsAllUpserted()
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
        var result = await saver.UpsertAsync(all);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
    }

    [Fact]
    public async Task UpsertAsync_OriginalIndex_PreservedAcrossPartitions()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var builder = new TestDataBuilder();
        var products = builder.CreateValidProducts(6);
        foreach (var p in products) p.Id = 0;

        var result = await saver.UpsertAsync(products);

        var indices = result.AllUpsertedEntities.Select(e => e.OriginalIndex).OrderBy(i => i).ToList();
        indices.ShouldBe(Enumerable.Range(0, 6).ToList());
    }

    [Fact]
    public async Task UpdateAsync_EmptyCollection_ReturnsEmptyResult()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var result = await saver.UpdateAsync(new List<Product>());

        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
    }

    [Fact]
    public async Task UpdateAsync_SingleEntity_Works()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 1));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        products[0].Price += 5;

        var result = await saver.UpdateAsync(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);
    }

    [Fact]
    public async Task UpdateAsync_RoundTrips_SummedAcrossPartitions()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        var result = await saver.UpdateAsync(products);

        result.DatabaseRoundTrips.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task UpdateAsync_Duration_ReflectsWallClockTime()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 6));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        var result = await saver.UpdateAsync(products);

        result.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    // === Sync method tests ===

    [Fact]
    public void Update_Sync_AllSucceed()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        var result = saver.Update(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
    }

    [Fact]
    public void Insert_Sync_AllSucceed()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = new TestDataBuilder().CreateValidProducts(4);
        foreach (var p in products) p.Id = 0;

        var result = saver.Insert(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
    }

    [Fact]
    public void Delete_Sync_AllSucceed()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());

        var result = saver.Delete(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
    }

    [Fact]
    public void Upsert_Sync_AllSucceed()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = new TestDataBuilder().CreateValidProducts(4);
        foreach (var p in products) p.Id = 0;

        var result = saver.Upsert(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
    }
}
