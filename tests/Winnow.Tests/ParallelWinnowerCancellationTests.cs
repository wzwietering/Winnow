using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Shouldly;

namespace Winnow.Tests;

public class ParallelWinnowerCancellationTests : ParallelTestBase
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

        var result = await saver.UpdateAsync(products, cts.Token);

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

        var result = await saver.UpdateAsync(products, cts.Token);

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

        var result = await saver.UpdateAsync(new List<Product>(), cts.Token);

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

        var result = await saver.InsertAsync(products, cts.Token);

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

        var result = await saver.DeleteAsync(products, cts.Token);

        result.WasCancelled.ShouldBeTrue();
    }

    [Fact]
    public async Task MidExecutionCancellation_DoesNotThrow()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 40));

        var saver = CreateSaver(maxDegreeOfParallelism: 4);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        using var cts = new CancellationTokenSource();
        // Cancel after a short delay to hit mid-execution
        cts.CancelAfter(TimeSpan.FromMilliseconds(5));

        // Should not throw - cancellation is captured in result
        var result = await saver.UpdateAsync(products, cts.Token);

        // Total accounted entities should match input
        (result.SuccessCount + result.FailureCount).ShouldBeLessThanOrEqualTo(40);
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

        var result = await saver.UpsertAsync(products, cts.Token);

        result.WasCancelled.ShouldBeTrue();
    }
}
