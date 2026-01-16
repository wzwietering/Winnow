using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests;

public class BatchSaverTests : TestBase
{
    [Fact]
    public void UpdateAllEntities_WithNoFailures_ReturnsAllSuccessful()
    {
        using var context = CreateContext();
        SeedData(context, 10);

        var productsToUpdate = context.Products.Take(10).ToList();
        foreach (var product in productsToUpdate)
        {
            product.Price += 5.00m;
        }

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpdateBatch(productsToUpdate);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(10);
        result.FailureCount.ShouldBe(0);
        result.SuccessfulIds.Count.ShouldBe(10);
    }

    [Fact]
    public void UpdateEmptyCollection_ReturnsEmptyResult()
    {
        using var context = CreateContext();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpdateBatch(new List<Product>());

        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
        result.TotalProcessed.ShouldBe(0);
    }

    [Fact]
    public void UpdateSingleEntity_ReturnsSuccess()
    {
        using var context = CreateContext();
        SeedData(context, 1);

        var product = context.Products.First();
        product.Price += 10.00m;

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpdateBatch(new List<Product> { product });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);
        result.SuccessfulIds.ShouldContain(product.Id);
    }

    [Fact]
    public void UpdateEntities_WithInvalidPrice_TracksFailedIds()
    {
        using var context = CreateContext();
        SeedData(context, 5);

        var productsToUpdate = context.Products.Take(5).ToList();
        productsToUpdate[0].Price = -10.00m;
        productsToUpdate[1].Price = 50.00m;
        productsToUpdate[2].Price = -5.00m;
        productsToUpdate[3].Price = 100.00m;
        productsToUpdate[4].Price = 25.00m;

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpdateBatch(productsToUpdate);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.FailureCount.ShouldBe(2);
        result.FailedIds.ShouldContain(productsToUpdate[0].Id);
        result.FailedIds.ShouldContain(productsToUpdate[2].Id);
        result.Failures.All(f => f.Reason == FailureReason.ValidationError).ShouldBeTrue();
    }

    [Fact]
    public void UpdateEntities_WithInvalidStock_TracksFailedIds()
    {
        using var context = CreateContext();
        SeedData(context, 3);

        var productsToUpdate = context.Products.Take(3).ToList();
        productsToUpdate[0].Stock = -5;
        productsToUpdate[1].Stock = 100;
        productsToUpdate[2].Stock = -10;

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpdateBatch(productsToUpdate);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);
        result.FailureCount.ShouldBe(2);
        result.FailedIds.ShouldContain(productsToUpdate[0].Id);
        result.FailedIds.ShouldContain(productsToUpdate[2].Id);
    }

    [Fact]
    public void UpdateMixedEntities_WithSomeInvalid_ReturnsPartialSuccess()
    {
        using var context = CreateContext();
        SeedData(context, 10);

        var productsToUpdate = context.Products.Take(10).ToList();
        productsToUpdate[2].Price = -1.00m;
        productsToUpdate[5].Stock = -1;
        productsToUpdate[8].Price = -10.00m;

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpdateBatch(productsToUpdate);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(7);
        result.FailureCount.ShouldBe(3);
        result.TotalProcessed.ShouldBe(10);
    }

    [Fact]
    public void UpdateLargeBatch_1000Entities_CompletesSuccessfully()
    {
        using var context = CreateContext();
        SeedData(context, 1000);

        var productsToUpdate = context.Products.Take(1000).ToList();
        foreach (var product in productsToUpdate)
        {
            product.Price += 1.00m;
        }

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpdateBatch(productsToUpdate);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1000);
        result.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void UpdateEntities_WithNullCollection_ThrowsArgumentException()
    {
        using var context = CreateContext();
        var saver = new BatchSaver<Product, int>(context);

        Should.Throw<ArgumentNullException>(() => saver.UpdateBatch(null!));
    }

    [Fact]
    public void UpdateEntities_WithOneByOneStrategy_ProcessesIndividually()
    {
        using var context = CreateContext();
        SeedData(context, 5);

        var productsToUpdate = context.Products.Take(5).ToList();
        productsToUpdate[0].Price = -10.00m;
        productsToUpdate[2].Stock = -5;

        var options = new BatchOptions { Strategy = BatchStrategy.OneByOne };
        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpdateBatch(productsToUpdate, options);

        result.SuccessCount.ShouldBe(3);
        result.FailureCount.ShouldBe(2);
        result.DatabaseRoundTrips.ShouldBe(5);
    }

    [Fact]
    public void UpdateEntities_WithDivideAndConquerStrategy_IsolatesFailures()
    {
        using var context = CreateContext();
        SeedData(context, 8);

        var productsToUpdate = context.Products.Take(8).ToList();
        productsToUpdate[3].Price = -10.00m;

        var options = new BatchOptions { Strategy = BatchStrategy.DivideAndConquer };
        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpdateBatch(productsToUpdate, options);

        result.SuccessCount.ShouldBe(7);
        result.FailureCount.ShouldBe(1);
        result.FailedIds.ShouldContain(productsToUpdate[3].Id);
        result.DatabaseRoundTrips.ShouldBeLessThan(8);
    }
}
