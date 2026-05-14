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
        result.Failures.ShouldHaveSingleItem().EntityId.ShouldBe(products[1].Id);

        // The validated-out entity should still be in the database.
        context.ChangeTracker.Clear();
        context.Products.Find(products[1].Id).ShouldNotBeNull();
    }
}
