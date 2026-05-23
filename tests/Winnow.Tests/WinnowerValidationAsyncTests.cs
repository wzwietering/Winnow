using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

public class WinnowerValidationAsyncTests : TestBase
{
    private static WinnowValidator<Product> RejectNonPositivePrice()
        => (Product p, ref ValidationCollector c) =>
        {
            if (p.Price <= 0) c.Add("Price", "Must be positive");
        };

    [Fact]
    public async Task InsertAsync_PreValidation_BehavesIdenticallyToSync()
    {
        using var context = CreateContext();
        var products = new[]
        {
            new Product { Name = "A", Price = 10m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "B", Price = -1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
        };

        var options = new InsertOptions();
        options.WithValidation(RejectNonPositivePrice());

        var saver = new Winnower<Product, int>(context);
        var result = await saver.InsertAsync(products, options);

        result.SuccessCount.ShouldBe(1);
        result.FailureCount.ShouldBe(1);
        result.Failures.ShouldHaveSingleItem().EntityIndex.ShouldBe(1);
    }

    [Fact]
    public async Task InsertAsync_CancelledBeforeStart_ThrowsOperationCanceled()
    {
        using var context = CreateContext();
        var products = LargeProductBatch();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var options = new InsertOptions();
        options.WithValidation<Product>((Product _, ref ValidationCollector _) => { });
        options.Validation!.CancellationCheckInterval = 1;

        var saver = new Winnower<Product, int>(context);
        await Should.ThrowAsync<OperationCanceledException>(
            () => saver.InsertAsync(products, options, cts.Token));
    }

    [Fact]
    public async Task InsertAsync_CancelledMidValidation_ThrowsOperationCanceled()
    {
        using var context = CreateContext();
        var products = LargeProductBatch();

        using var cts = new CancellationTokenSource();

        // Validator counts invocations and cancels after the first poll has passed.
        int seen = 0;
        var options = new InsertOptions();
        options.WithValidation<Product>((Product _, ref ValidationCollector _) =>
        {
            if (Interlocked.Increment(ref seen) == 100) cts.Cancel();
        });
        options.Validation!.CancellationCheckInterval = 1;

        var saver = new Winnower<Product, int>(context);
        await Should.ThrowAsync<OperationCanceledException>(
            () => saver.InsertAsync(products, options, cts.Token));
    }

    [Fact]
    public async Task UpdateAsync_PreValidation_RecordsFailureByEntityId()
    {
        using var context = CreateContext();
        SeedData(context, 3);
        var products = context.Products.AsNoTracking().ToList();
        products[1].Price = -1m;

        var options = new WinnowOptions();
        options.WithValidation(RejectNonPositivePrice());

        var saver = new Winnower<Product, int>(context);
        var result = await saver.UpdateAsync(products, options);

        result.SuccessCount.ShouldBe(2);
        result.FailureCount.ShouldBe(1);
        var failure = result.Failures.ShouldHaveSingleItem();
        failure.EntityId.ShouldBe(products[1].Id);
        failure.Reason.ShouldBe(FailureReason.PreValidationError);
        failure.ValidationErrors.ShouldNotBeNull();
        failure.ValidationErrors!.ShouldContain(e => e.PropertyName == "Price");
    }

    [Fact]
    public async Task DeleteAsync_PreValidation_RecordsFailureAndEntityStaysInDb()
    {
        using var context = CreateContext();
        SeedData(context, 3);
        var products = context.Products.AsNoTracking().ToList();
        var invalidId = products[1].Id;
        products[1].Price = -1m;

        var options = new DeleteOptions();
        options.WithValidation(RejectNonPositivePrice());

        var saver = new Winnower<Product, int>(context);
        var result = await saver.DeleteAsync(products, options);

        result.SuccessCount.ShouldBe(2);
        result.FailureCount.ShouldBe(1);
        var failure = result.Failures.ShouldHaveSingleItem();
        failure.EntityId.ShouldBe(invalidId);
        failure.Reason.ShouldBe(FailureReason.PreValidationError);

        context.ChangeTracker.Clear();
        context.Products.Find(invalidId).ShouldNotBeNull();
    }

    [Fact]
    public async Task UpdateAsync_CancelledBeforeStart_ThrowsOperationCanceled()
    {
        using var context = CreateContext();
        SeedData(context, 1);
        var products = context.Products.AsNoTracking().ToList();
        // pad with synthetic instances to exercise the cancellation poll
        for (int i = 0; i < 100; i++) products.Add(new Product { Id = i + 1000, Name = "x", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var options = new WinnowOptions();
        options.WithValidation<Product>((Product _, ref ValidationCollector _) => { });
        options.Validation!.CancellationCheckInterval = 1;

        var saver = new Winnower<Product, int>(context);
        await Should.ThrowAsync<OperationCanceledException>(
            () => saver.UpdateAsync(products, options, cts.Token));
    }

    [Fact]
    public async Task DeleteAsync_CancelledBeforeStart_ThrowsOperationCanceled()
    {
        using var context = CreateContext();
        SeedData(context, 1);
        var products = context.Products.AsNoTracking().ToList();
        for (int i = 0; i < 100; i++) products.Add(new Product { Id = i + 1000, Name = "x", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var options = new DeleteOptions();
        options.WithValidation<Product>((Product _, ref ValidationCollector _) => { });
        options.Validation!.CancellationCheckInterval = 1;

        var saver = new Winnower<Product, int>(context);
        await Should.ThrowAsync<OperationCanceledException>(
            () => saver.DeleteAsync(products, options, cts.Token));
    }

    [Fact]
    public async Task UpsertAsync_CancelledBeforeStart_ThrowsOperationCanceled()
    {
        using var context = CreateContext();
        var products = LargeProductBatch();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var options = new UpsertOptions();
        options.WithValidation<Product>((Product _, ref ValidationCollector _) => { });
        options.Validation!.CancellationCheckInterval = 1;

        var saver = new Winnower<Product, int>(context);
        await Should.ThrowAsync<OperationCanceledException>(
            () => saver.UpsertAsync(products, options, cts.Token));
    }

    private static List<Product> LargeProductBatch() =>
        Enumerable.Range(0, 1_000).Select(i => new Product
        {
            Name = $"p{i}",
            Price = 1m,
            Stock = 1,
            LastModified = DateTimeOffset.UtcNow,
        }).ToList();
}
