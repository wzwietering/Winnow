using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Shouldly;

namespace EfCoreUtils.Tests;

public class ParallelBatchSaverCancellationTests : ParallelTestBase
{
    [Fact]
    public async Task PreCancelledToken_ReturnsWithWasCancelled()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await saver.UpdateBatchAsync(products, cts.Token);

        result.WasCancelled.ShouldBeTrue();
    }

    [Fact]
    public async Task PreCancelledToken_SuccessCountReflectsOnlyCompleted()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await saver.UpdateBatchAsync(products, cts.Token);

        // With pre-cancelled token, no partitions should complete
        result.SuccessCount.ShouldBe(0);
    }

    [Fact]
    public async Task EmptyEntities_WithCancelledToken_ReturnsEmptyResult()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await saver.UpdateBatchAsync(new List<Product>(), cts.Token);

        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
    }

    [Fact]
    public async Task CancellationWorks_ForInsertOperation()
    {
        EnsureDatabaseCreated();

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = new TestDataBuilder().CreateValidProducts(4);
        foreach (var p in products) p.Id = 0;

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await saver.InsertBatchAsync(products, cts.Token);

        result.WasCancelled.ShouldBeTrue();
    }

    [Fact]
    public async Task CancellationWorks_ForDeleteOperation()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await saver.DeleteBatchAsync(products, cts.Token);

        result.WasCancelled.ShouldBeTrue();
    }

    [Fact]
    public async Task CancellationWorks_ForUpsertOperation()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await saver.UpsertBatchAsync(products, cts.Token);

        result.WasCancelled.ShouldBeTrue();
    }
}
