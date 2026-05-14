using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

public class WinnowerValidationAsyncTests : TestBase
{
    private static ValidatorDelegate<Product> RejectNonPositivePrice()
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
    public async Task InsertAsync_CancelledMidValidation_ThrowsOperationCanceled()
    {
        using var context = CreateContext();
        // Many entities so the cancellation poll fires; the validator just yields.
        var products = Enumerable.Range(0, 10_000).Select(i => new Product
        {
            Name = $"p{i}",
            Price = 1m,
            Stock = 1,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var options = new InsertOptions();
        options.WithValidation<Product>((Product _, ref ValidationCollector _) => { });
        options.Validation!.CancellationCheckInterval = 1; // poll every entity

        var saver = new Winnower<Product, int>(context);
        await Should.ThrowAsync<OperationCanceledException>(
            () => saver.InsertAsync(products, options, cts.Token));
    }
}
