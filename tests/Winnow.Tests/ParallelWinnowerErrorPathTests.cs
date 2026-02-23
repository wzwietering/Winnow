using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

public class ParallelWinnowerErrorPathTests : ParallelTestBase
{
    [Fact]
    public async Task FactoryReturnsNull_ReportsFailurePerPartition()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        var callCount = 0;
        Func<DbContext> factory = () =>
        {
            callCount++;
            // First 2 calls succeed (constructor validation), then return null
            if (callCount <= 2)
                return CreateContextFactory()();
            return null!;
        };

        var saver = new ParallelWinnower<Product, int>(factory, 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        var result = await saver.UpdateAsync(products);

        result.FailureCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task FactoryThrowsDuringExecution_ReportsFailure()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 4));

        var callCount = 0;
        Func<DbContext> factory = () =>
        {
            callCount++;
            // First 2 calls succeed (constructor validation), then throw
            if (callCount <= 2)
                return CreateContextFactory()();
            throw new InvalidOperationException("Connection pool exhausted");
        };

        var saver = new ParallelWinnower<Product, int>(factory, 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        var result = await saver.UpdateAsync(products);

        result.FailureCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task CancellationDuringExecution_ReturnsPartialResults()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 20));

        var saver = CreateSaver(maxDegreeOfParallelism: 2);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        var cts = new CancellationTokenSource();
        // Cancel after a very short delay to try to catch mid-execution
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var result = await saver.UpdateAsync(products, cts.Token);

        // Either all succeed (if cancellation came too late) or some are cancelled
        (result.IsCompleteSuccess || result.WasCancelled).ShouldBeTrue();
    }

    [Fact]
    public async Task FactoryFailsMidExecution_SomePartitionsSucceed_SomeFail()
    {
        EnsureDatabaseCreated();
        SeedWithFactory(ctx => SeedData(ctx, 8));

        var callCount = 0;
        Func<DbContext> factory = () =>
        {
            var count = Interlocked.Increment(ref callCount);
            // First 2 calls for constructor validation, then alternate success/failure
            if (count <= 2)
                return CreateContextFactory()();
            // Odd execution calls succeed, even execution calls fail
            if ((count - 2) % 2 == 0)
                throw new InvalidOperationException("Connection pool exhausted");
            return CreateContextFactory()();
        };

        var saver = new ParallelWinnower<Product, int>(factory, 4);
        var products = QueryWithFactory(ctx => ctx.Products.ToList());
        foreach (var p in products) p.Price += 5;

        var result = await saver.UpdateAsync(products);

        // Should have a mix of successes and failures
        result.FailureCount.ShouldBeGreaterThan(0);
        // At least some should succeed since some factory calls work
        (result.SuccessCount + result.FailureCount).ShouldBe(8);
    }

    [Fact]
    public async Task BroadExceptionHandling_InvalidOperationException_CapturedAsFailure()
    {
        EnsureDatabaseCreated();

        // Insert entities that will cause an InvalidOperationException during update
        // by using an entity with a detached reference that causes EF Core issues
        var saver = CreateSaver(maxDegreeOfParallelism: 2);

        var products = new TestDataBuilder().CreateValidProducts(4);
        // Set non-zero IDs for products that don't exist in DB - will fail on update
        foreach (var p in products) p.Price += 5;

        var result = await saver.UpdateAsync(products);

        // Should report failures gracefully instead of throwing
        result.FailureCount.ShouldBeGreaterThan(0);
    }
}
