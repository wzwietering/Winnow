using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

public class WinnowerUpdateValidationTests : TestBase
{
    [Fact]
    public void Update_PreValidation_RecordsFailureByEntityId()
    {
        using var context = CreateContext();
        SeedData(context, 3);
        var products = context.Products.AsNoTracking().ToList();
        products[1].Price = -1m; // mark as invalid

        var options = new WinnowOptions();
        options.WithValidation<Product>((Product p, ref ValidationCollector c) =>
        {
            if (p.Price <= 0) c.Add(nameof(Product.Price), "Must be positive");
        });

        var saver = new Winnower<Product, int>(context);
        var result = saver.Update(products, options);

        result.SuccessCount.ShouldBe(2);
        result.FailureCount.ShouldBe(1);
        var failure = result.Failures.ShouldHaveSingleItem();
        failure.EntityId.ShouldBe(products[1].Id);
        failure.Reason.ShouldBe(FailureReason.ValidationError);
        failure.ValidationErrors.ShouldNotBeNull();
        failure.ValidationErrors!.ShouldContain(e => e.PropertyName == nameof(Product.Price));
    }

    [Fact]
    public void Update_FailureBehaviorThrow_ThrowsWinnowValidationException()
    {
        using var context = CreateContext();
        SeedData(context, 2);
        var products = context.Products.AsNoTracking().ToList();
        products[0].Price = -1m;

        var options = new WinnowOptions();
        options.WithValidation<Product>((Product p, ref ValidationCollector c) =>
        {
            if (p.Price <= 0) c.Add(nameof(Product.Price), "Must be positive");
        });
        options.Validation!.FailureBehavior = ValidationFailureBehavior.Throw;

        var saver = new Winnower<Product, int>(context);
        var ex = Should.Throw<WinnowValidationException>(() => saver.Update(products, options));

        ex.Failures.Count.ShouldBe(1);
        ex.Failures[0].EntityIndex.ShouldBe(0);
    }

    [Fact]
    public void Update_AllValid_NoFailures()
    {
        using var context = CreateContext();
        SeedData(context, 3);
        var products = context.Products.AsNoTracking().ToList();
        foreach (var p in products) p.Stock += 1;

        var options = new WinnowOptions();
        options.WithValidation<Product>((Product p, ref ValidationCollector c) =>
        {
            if (p.Price <= 0) c.Add("Price", "Must be positive");
        });

        var saver = new Winnower<Product, int>(context);
        var result = saver.Update(products, options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
    }

    [Fact]
    public void Update_AllInvalid_ZeroDatabaseRoundTrips()
    {
        using var context = CreateContext();
        SeedData(context, 3);
        var products = context.Products.AsNoTracking().ToList();
        foreach (var p in products) p.Price = -1m;

        var options = new WinnowOptions();
        options.WithValidation<Product>((Product p, ref ValidationCollector c) =>
        {
            if (p.Price <= 0) c.Add(nameof(Product.Price), "Must be positive");
        });

        var saver = new Winnower<Product, int>(context);
        var result = saver.Update(products, options);

        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(3);
        result.DatabaseRoundTrips.ShouldBe(0);
    }

    [Fact]
    public void Update_EmptyBatch_NoFailuresNoRoundTrips()
    {
        using var context = CreateContext();
        var options = new WinnowOptions();
        options.WithValidation<Product>((Product _, ref ValidationCollector _) => { });

        var saver = new Winnower<Product, int>(context);
        var result = saver.Update(Array.Empty<Product>(), options);

        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
        result.DatabaseRoundTrips.ShouldBe(0);
    }
}
