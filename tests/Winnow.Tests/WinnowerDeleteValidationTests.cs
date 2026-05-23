using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

public class WinnowerDeleteValidationTests : TestBase
{
    [Fact]
    public void Delete_PreValidation_RecordsFailureByEntityIdAndSkipsDelete()
    {
        using var context = CreateContext();
        SeedData(context, 3);
        var products = context.Products.AsNoTracking().ToList();

        var options = new DeleteOptions();
        options.WithValidation<Product>((Product p, ref ValidationCollector c) =>
        {
            if (p.Id == products[1].Id) c.Add("Id", "Refusing to delete protected entity");
        });

        var saver = new Winnower<Product, int>(context);
        var result = saver.Delete(products, options);

        result.SuccessCount.ShouldBe(2);
        result.FailureCount.ShouldBe(1);
        var failure = result.Failures.ShouldHaveSingleItem();
        failure.EntityId.ShouldBe(products[1].Id);
        failure.ValidationErrors.ShouldNotBeNull();
        failure.ValidationErrors!.ShouldContain(e => e.PropertyName == "Id");

        // The validated-out entity should still be in the database.
        context.ChangeTracker.Clear();
        context.Products.Find(products[1].Id).ShouldNotBeNull();
    }

    [Fact]
    public void Delete_FailureBehaviorThrow_ThrowsWinnowValidationException()
    {
        using var context = CreateContext();
        SeedData(context, 2);
        var products = context.Products.AsNoTracking().ToList();

        var options = new DeleteOptions();
        options.WithValidation<Product>((Product p, ref ValidationCollector c) =>
        {
            if (p.Id == products[0].Id) c.Add("Id", "Refusing");
        });
        options.Validation!.FailureBehavior = ValidationFailureBehavior.Throw;

        var saver = new Winnower<Product, int>(context);
        var ex = Should.Throw<WinnowValidationException>(() => saver.Delete(products, options));

        ex.Failures.Count.ShouldBe(1);
        ex.Failures[0].EntityIndex.ShouldBe(0);
        context.ChangeTracker.Clear();
        context.Products.Count().ShouldBe(2);
    }

    [Fact]
    public void Delete_AllInvalid_ZeroDatabaseRoundTrips()
    {
        using var context = CreateContext();
        SeedData(context, 3);
        var products = context.Products.AsNoTracking().ToList();

        var options = new DeleteOptions();
        options.WithValidation<Product>((Product _, ref ValidationCollector c) =>
            c.Add("Id", "Refusing all deletes"));

        var saver = new Winnower<Product, int>(context);
        var result = saver.Delete(products, options);

        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(3);
        result.DatabaseRoundTrips.ShouldBe(0);
        context.ChangeTracker.Clear();
        context.Products.Count().ShouldBe(3);
    }

    [Fact]
    public void Delete_EmptyBatch_NoFailuresNoRoundTrips()
    {
        using var context = CreateContext();
        var options = new DeleteOptions();
        options.WithValidation<Product>((Product _, ref ValidationCollector _) => { });

        var saver = new Winnower<Product, int>(context);
        var result = saver.Delete(Array.Empty<Product>(), options);

        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
        result.DatabaseRoundTrips.ShouldBe(0);
    }
}
