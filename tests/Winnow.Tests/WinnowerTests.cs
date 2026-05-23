using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

public class WinnowerTests : TestBase
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

        var saver = new Winnower<Product, int>(context);
        var result = saver.Update(productsToUpdate);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(10);
        result.FailureCount.ShouldBe(0);
        result.SuccessfulIds.Count.ShouldBe(10);
    }

    [Fact]
    public void UpdateEmptyCollection_ReturnsEmptyResult()
    {
        using var context = CreateContext();

        var saver = new Winnower<Product, int>(context);
        var result = saver.Update(new List<Product>());

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

        var saver = new Winnower<Product, int>(context);
        var result = saver.Update(new List<Product> { product });

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

        var saver = new Winnower<Product, int>(context);
        var result = saver.Update(productsToUpdate);

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

        var saver = new Winnower<Product, int>(context);
        var result = saver.Update(productsToUpdate);

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

        var saver = new Winnower<Product, int>(context);
        var result = saver.Update(productsToUpdate);

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

        var saver = new Winnower<Product, int>(context);
        var result = saver.Update(productsToUpdate);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1000);
        result.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void Update_WithNullCollection_ThrowsArgumentNullException()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        Should.Throw<ArgumentNullException>(() => saver.Update(null!));
    }

    [Fact]
    public async Task UpdateAsync_WithNullCollection_ThrowsArgumentNullException()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        await Should.ThrowAsync<ArgumentNullException>(() => saver.UpdateAsync(null!));
    }

    [Fact]
    public void Insert_WithNullCollection_ThrowsArgumentNullException()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        Should.Throw<ArgumentNullException>(() => saver.Insert(null!));
    }

    [Fact]
    public async Task InsertAsync_WithNullCollection_ThrowsArgumentNullException()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        await Should.ThrowAsync<ArgumentNullException>(() => saver.InsertAsync(null!));
    }

    [Fact]
    public void Delete_WithNullCollection_ThrowsArgumentNullException()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        Should.Throw<ArgumentNullException>(() => saver.Delete(null!));
    }

    [Fact]
    public async Task DeleteAsync_WithNullCollection_ThrowsArgumentNullException()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        await Should.ThrowAsync<ArgumentNullException>(() => saver.DeleteAsync(null!));
    }

    [Fact]
    public void Upsert_WithNullCollection_ThrowsArgumentNullException()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        Should.Throw<ArgumentNullException>(() => saver.Upsert(null!));
    }

    [Fact]
    public async Task UpsertAsync_WithNullCollection_ThrowsArgumentNullException()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        await Should.ThrowAsync<ArgumentNullException>(() => saver.UpsertAsync(null!));
    }

    [Fact]
    public void UpdateGraph_WithNullCollection_ThrowsArgumentNullException()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        Should.Throw<ArgumentNullException>(() => saver.UpdateGraph(null!));
    }

    [Fact]
    public async Task UpdateGraphAsync_WithNullCollection_ThrowsArgumentNullException()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        await Should.ThrowAsync<ArgumentNullException>(() => saver.UpdateGraphAsync(null!));
    }

    [Fact]
    public void InsertGraph_WithNullCollection_ThrowsArgumentNullException()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        Should.Throw<ArgumentNullException>(() => saver.InsertGraph(null!));
    }

    [Fact]
    public async Task InsertGraphAsync_WithNullCollection_ThrowsArgumentNullException()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        await Should.ThrowAsync<ArgumentNullException>(() => saver.InsertGraphAsync(null!));
    }

    [Fact]
    public void DeleteGraph_WithNullCollection_ThrowsArgumentNullException()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        Should.Throw<ArgumentNullException>(() => saver.DeleteGraph(null!));
    }

    [Fact]
    public async Task DeleteGraphAsync_WithNullCollection_ThrowsArgumentNullException()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        await Should.ThrowAsync<ArgumentNullException>(() => saver.DeleteGraphAsync(null!));
    }

    [Fact]
    public void UpsertGraph_WithNullCollection_ThrowsArgumentNullException()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        Should.Throw<ArgumentNullException>(() => saver.UpsertGraph(null!));
    }

    [Fact]
    public async Task UpsertGraphAsync_WithNullCollection_ThrowsArgumentNullException()
    {
        using var context = CreateContext();
        var saver = new Winnower<Product, int>(context);

        await Should.ThrowAsync<ArgumentNullException>(() => saver.UpsertGraphAsync(null!));
    }

    [Fact]
    public void UpdateEntities_WithOneByOneStrategy_ProcessesIndividually()
    {
        using var context = CreateContext();
        SeedData(context, 5);

        var productsToUpdate = context.Products.Take(5).ToList();
        productsToUpdate[0].Price = -10.00m;
        productsToUpdate[2].Stock = -5;

        var options = new WinnowOptions { Strategy = BatchStrategy.OneByOne };
        var saver = new Winnower<Product, int>(context);
        var result = saver.Update(productsToUpdate, options);

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

        var options = new WinnowOptions { Strategy = BatchStrategy.DivideAndConquer };
        var saver = new Winnower<Product, int>(context);
        var result = saver.Update(productsToUpdate, options);

        result.SuccessCount.ShouldBe(7);
        result.FailureCount.ShouldBe(1);
        result.FailedIds.ShouldContain(productsToUpdate[3].Id);
        result.DatabaseRoundTrips.ShouldBeLessThan(8);
    }
}
