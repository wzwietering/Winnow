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
        var ex = Should.Throw<WinnowValidationException>(() => saver.Insert(products, options));
        ex.Failures.Count.ShouldBe(1);
        ex.Failures[0].EntityIndex.ShouldBe(0);
    }

    [Fact]
    public void Throw_WithMultipleFailures_MessageIncludesFailingIndices()
    {
        using var context = CreateContext();
        var products = new List<Product>();
        for (int i = 0; i < 5; i++)
        {
            products.Add(new Product
            {
                Name = $"p{i}",
                Price = (i == 1 || i == 3) ? -1m : 1m,
                Stock = 1,
                LastModified = DateTimeOffset.UtcNow
            });
        }

        var options = new InsertOptions();
        options.WithValidation(RejectNonPositivePrice());
        options.Validation!.FailureBehavior = ValidationFailureBehavior.Throw;

        var saver = new Winnower<Product, int>(context);
        var ex = Should.Throw<WinnowValidationException>(() => saver.Insert(products, options));

        ex.Failures.Count.ShouldBe(2);
        ex.Message.ShouldContain("1");
        ex.Message.ShouldContain("3");
    }

    [Fact]
    public void WinnowValidationException_RequiresNonEmptyFailures()
    {
        Should.Throw<ArgumentNullException>(() =>
            new WinnowValidationException(null!));

        Should.Throw<ArgumentException>(() =>
            new WinnowValidationException(Array.Empty<EntityValidationFailure>()));
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
    public void Insert_NullEntityInList_RecordsValidationFailureAndDoesNotReachDatabase()
    {
        using var context = CreateContext();
        var products = new Product[]
        {
            new() { Name = "A", Price = 10m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
            null!,
            new() { Name = "C", Price = 5m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
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

        products[0].Id.ShouldBeGreaterThan(0);
        products[2].Id.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Insert_ValidationOptionsForWrongEntityType_ThrowsInvalidOperationException()
    {
        using var context = CreateContext();
        var products = new[]
        {
            new Product { Name = "A", Price = 10m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
        };

        // Configure validation for Order, then apply to a Product insert.
        var options = new InsertOptions();
        options.WithValidation<Order>((Order _, ref ValidationCollector _) => { });

        var saver = new Winnower<Product, int>(context);

        var ex = Should.Throw<InvalidOperationException>(() => saver.Insert(products, options));
        ex.Message.ShouldContain("Order");
        ex.Message.ShouldContain("Product");
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

    [Fact]
    public void Insert_OneByOneStrategy_MixedValidity_PreservesOriginalIndices()
    {
        using var context = CreateContext();
        var products = new List<Product>();
        for (int i = 0; i < 6; i++)
        {
            products.Add(new Product
            {
                Name = $"p{i}",
                Price = (i % 2 == 0) ? 1m : -1m,
                Stock = 1,
                LastModified = DateTimeOffset.UtcNow,
            });
        }

        var options = new InsertOptions { Strategy = BatchStrategy.OneByOne };
        options.WithValidation(RejectNonPositivePrice());

        var saver = new Winnower<Product, int>(context);
        var result = saver.Insert(products, options);

        result.SuccessCount.ShouldBe(3);
        result.FailureCount.ShouldBe(3);
        result.Failures.Select(f => f.EntityIndex).OrderBy(x => x).ShouldBe([1, 3, 5]);
    }

    [Fact]
    public void Insert_EmptyList_NoFailuresNoSurvivors()
    {
        using var context = CreateContext();
        var options = new InsertOptions();
        options.WithValidation(RejectNonPositivePrice());

        var saver = new Winnower<Product, int>(context);
        var result = saver.Insert(Array.Empty<Product>(), options);

        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
    }

    [Fact]
    public void Insert_ValidatorThrows_PropagatesException()
    {
        using var context = CreateContext();
        var products = new[]
        {
            new Product { Name = "A", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
        };

        var options = new InsertOptions();
        options.WithValidation<Product>((Product _, ref ValidationCollector _) =>
            throw new InvalidOperationException("validator misconfigured"));

        var saver = new Winnower<Product, int>(context);
        var ex = Should.Throw<InvalidOperationException>(() => saver.Insert(products, options));
        ex.Message.ShouldBe("validator misconfigured");
    }

    [Fact]
    public void Throw_EntityValidationFailure_CarriesStructuredErrors()
    {
        using var context = CreateContext();
        var products = new[]
        {
            new Product { Name = "A", Price = -1m, Stock = -5, LastModified = DateTimeOffset.UtcNow },
        };

        var options = new InsertOptions();
        options.WithValidation<Product>((Product p, ref ValidationCollector c) =>
        {
            if (p.Price <= 0) c.Add(nameof(Product.Price), "Must be positive", "RANGE");
            if (p.Stock < 0) c.Add(nameof(Product.Stock), "Cannot be negative", "RANGE");
        });
        options.Validation!.FailureBehavior = ValidationFailureBehavior.Throw;

        var saver = new Winnower<Product, int>(context);
        var ex = Should.Throw<WinnowValidationException>(() => saver.Insert(products, options));

        var failure = ex.Failures.ShouldHaveSingleItem();
        failure.Errors.Count.ShouldBe(2);
        failure.Errors.ShouldContain(e => e.PropertyName == nameof(Product.Price) && e.Code == "RANGE");
        failure.Errors.ShouldContain(e => e.PropertyName == nameof(Product.Stock) && e.Code == "RANGE");
    }

    [Fact]
    public void Insert_EntityLevelError_FormatsWithoutPropertyPrefix()
    {
        using var context = CreateContext();
        var products = new[]
        {
            new Product { Name = "A", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow },
        };

        var options = new InsertOptions();
        options.WithValidation<Product>((Product _, ref ValidationCollector c) =>
            c.Add(string.Empty, "Cross-field rule rejected"));

        var saver = new Winnower<Product, int>(context);
        var result = saver.Insert(products, options);

        var failure = result.Failures.ShouldHaveSingleItem();
        failure.ErrorMessage.ShouldBe("Cross-field rule rejected");
        failure.ErrorMessage.ShouldNotContain(":");
    }

    [Fact]
    public void Insert_DataAnnotations_AnnotatedEntity_RejectsInvalidAndSucceedsValid()
    {
        using var context = CreateContext();
        var products = new[]
        {
            new AnnotatedProduct { Name = "A", Quantity = 5 },
            new AnnotatedProduct { Name = null, Quantity = 5 },
            new AnnotatedProduct { Name = "B", Quantity = -1 },
        };

        var options = new InsertOptions().WithDataAnnotations<AnnotatedProduct>();

        // Drive through the public pipeline via the runner directly (no DbContext registration needed).
        var failures = new List<(int Index, string Message, IReadOnlyList<ValidationError> Errors)>();
        Winnow.Internal.Validation.PreValidationRunner.Run(
            products.ToList(),
            options.Validation!,
            (idx, msg, errs) => failures.Add((idx, msg, errs)),
            navigationFilter: null,
            CancellationToken.None);

        failures.Count.ShouldBe(2);
        failures.Select(f => f.Index).OrderBy(x => x).ShouldBe([1, 2]);
        failures.ShouldContain(f => f.Errors.Any(e => e.PropertyName == nameof(AnnotatedProduct.Name)));
        failures.ShouldContain(f => f.Errors.Any(e => e.PropertyName == nameof(AnnotatedProduct.Quantity)));
    }

    private sealed class AnnotatedProduct
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string? Name { get; set; }

        [System.ComponentModel.DataAnnotations.Range(0, 100)]
        public int Quantity { get; set; }
    }
}
