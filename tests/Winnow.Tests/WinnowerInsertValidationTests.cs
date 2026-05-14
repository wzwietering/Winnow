using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

public class WinnowerInsertValidationTests : TestBase
{
    private static ValidatorDelegate<Product> RejectNonPositivePrice()
        => (Product p, ref ValidationCollector c) =>
        {
            if (p.Price <= 0) c.Add(nameof(Product.Price), "Must be positive");
        };

    [Fact]
    public void Insert_PreValidationCatchesInvalid_RecordsFailureAndSkipsSave()
    {
        using var context = CreateContext();
        var products = new[]
        {
            new Product { Name = "A", Price = 10m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "B", Price = -1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "C", Price = 5m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
        };

        var options = new InsertOptions();
        options.WithValidation(RejectNonPositivePrice());

        var saver = new Winnower<Product, int>(context);
        var result = saver.Insert(products, options);

        result.SuccessCount.ShouldBe(2);
        result.FailureCount.ShouldBe(1);
        var failure = result.Failures.ShouldHaveSingleItem();
        failure.EntityIndex.ShouldBe(1);
        failure.Reason.ShouldBe(FailureReason.ValidationError);
        failure.ErrorMessage.ShouldContain("Price");

        // The invalid entity was never assigned an ID.
        products[1].Id.ShouldBe(0);
        // The valid entities got IDs from EF Core.
        products[0].Id.ShouldBeGreaterThan(0);
        products[2].Id.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Insert_AllInvalid_SkipsAllAndRecordsAll()
    {
        using var context = CreateContext();
        var products = Enumerable.Range(0, 3).Select(i => new Product
        {
            Name = $"x{i}",
            Price = -1m,
            Stock = 1,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var options = new InsertOptions();
        options.WithValidation(RejectNonPositivePrice());

        var saver = new Winnower<Product, int>(context);
        var result = saver.Insert(products, options);

        result.IsCompleteFailure.ShouldBeTrue();
        result.FailureCount.ShouldBe(3);
        result.Failures.Select(f => f.EntityIndex).OrderBy(x => x).ShouldBe([0, 1, 2]);
        result.DatabaseRoundTrips.ShouldBe(0);
    }

    [Fact]
    public void Insert_AllValid_NoFailuresAndNoExtraRoundTrips()
    {
        using var context = CreateContext();
        var products = Enumerable.Range(1, 4).Select(i => new Product
        {
            Name = $"p{i}",
            Price = i,
            Stock = 1,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var options = new InsertOptions { Strategy = BatchStrategy.DivideAndConquer };
        options.WithValidation(RejectNonPositivePrice());

        var saver = new Winnower<Product, int>(context);
        var result = saver.Insert(products, options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
        result.FailureCount.ShouldBe(0);
    }

    [Fact]
    public void Insert_FailureBehaviorThrow_ThrowsValidationException()
    {
        using var context = CreateContext();
        var products = new[]
        {
            new Product { Name = "A", Price = -1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
        };

        var options = new InsertOptions();
        options.WithValidation(RejectNonPositivePrice());
        options.Validation!.FailureBehavior = ValidationFailureBehavior.Throw;

        var saver = new Winnower<Product, int>(context);
        var ex = Should.Throw<ValidationException>(() => saver.Insert(products, options));
        ex.Failures.Count.ShouldBe(1);
        ex.Failures[0].EntityIndex.ShouldBe(0);
    }

    [Fact]
    public void Insert_OriginalIndicesPreserved_AcrossDivideAndConquer()
    {
        using var context = CreateContext();
        // 8 entities, every other one invalid. With D&C, the strategy recurses;
        // we want the recorded EntityIndex to still match the user's input position.
        var products = new List<Product>();
        for (int i = 0; i < 8; i++)
        {
            products.Add(new Product
            {
                Name = $"p{i}",
                Price = (i % 2 == 0) ? 1m : -1m,
                Stock = 1,
                LastModified = DateTimeOffset.UtcNow
            });
        }

        var options = new InsertOptions { Strategy = BatchStrategy.DivideAndConquer };
        options.WithValidation(RejectNonPositivePrice());

        var saver = new Winnower<Product, int>(context);
        var result = saver.Insert(products, options);

        result.SuccessCount.ShouldBe(4);
        result.FailureCount.ShouldBe(4);
        result.Failures.Select(f => f.EntityIndex).OrderBy(x => x).ShouldBe([1, 3, 5, 7]);
    }

    [Fact]
    public void Insert_DataAnnotations_AttributeFreeEntity_Passthrough()
    {
        // Product carries no DataAnnotations attributes; the cached adapter
        // should resolve to a no-op validator and let everything through.
        using var context = CreateContext();
        var products = new[]
        {
            new Product { Name = "Good", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
        };

        var options = new InsertOptions();
        options.WithDataAnnotations<Product>();

        var saver = new Winnower<Product, int>(context);
        var result = saver.Insert(products, options);
        result.IsCompleteSuccess.ShouldBeTrue();
    }
}
