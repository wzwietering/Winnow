using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Shouldly;

namespace EfCoreUtils.Tests;

public class BatchSaverUpsertTests : TestBase
{
    [Fact]
    public void UpsertBatch_AllNew_AllInserted()
    {
        using var context = CreateContext();

        var products = Enumerable.Range(1, 5).Select(i => new Product
        {
            Name = $"New Product {i}",
            Price = 10.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(5);
        result.UpdatedCount.ShouldBe(0);
        result.InsertedIds.ShouldAllBe(id => id > 0);

        context.ChangeTracker.Clear();
        context.Products.Count().ShouldBe(5);
    }

    [Fact]
    public void UpsertBatch_AllExisting_AllUpdated()
    {
        using var context = CreateContext();
        SeedData(context, 5);

        var products = context.Products.ToList();
        foreach (var p in products)
            p.Price += 10.00m;

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(5);
        result.InsertedCount.ShouldBe(0);
        result.UpdatedIds.Count.ShouldBe(5);

        context.ChangeTracker.Clear();
        context.Products.Count().ShouldBe(5);
    }

    [Fact]
    public void UpsertBatch_Mixed_CorrectlyPartitioned()
    {
        using var context = CreateContext();
        SeedData(context, 3);

        var existingProducts = context.Products.ToList();
        foreach (var p in existingProducts)
            p.Price += 5.00m;

        var newProducts = Enumerable.Range(1, 2).Select(i => new Product
        {
            Name = $"New Mixed Product {i}",
            Price = 20.00m + i,
            Stock = 50,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(existingProducts.Concat(newProducts));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(2);
        result.UpdatedCount.ShouldBe(3);

        context.ChangeTracker.Clear();
        context.Products.Count().ShouldBe(5);
    }

    [Fact]
    public void UpsertBatch_EmptyCollection_ReturnsEmptyResult()
    {
        using var context = CreateContext();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch([]);

        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
        result.TotalProcessed.ShouldBe(0);
        result.InsertedCount.ShouldBe(0);
        result.UpdatedCount.ShouldBe(0);
    }

    [Fact]
    public void UpsertBatch_SingleNew_Inserted()
    {
        using var context = CreateContext();

        var product = new Product
        {
            Name = "Single New Product",
            Price = 25.00m,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        };

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch([product]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);
        result.UpdatedCount.ShouldBe(0);
        result.InsertedEntities[0].Id.ShouldBeGreaterThan(0);
        product.Id.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void UpsertBatch_SingleExisting_Updated()
    {
        using var context = CreateContext();
        SeedData(context, 1);

        var product = context.Products.First();
        var originalPrice = product.Price;
        product.Price += 15.00m;

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch([product]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(1);
        result.InsertedCount.ShouldBe(0);

        context.ChangeTracker.Clear();
        var updated = context.Products.Find(product.Id);
        updated!.Price.ShouldBe(originalPrice + 15.00m);
    }

    [Fact]
    public void UpsertBatch_Result_TracksInsertedEntities()
    {
        using var context = CreateContext();

        var products = Enumerable.Range(1, 3).Select(i => new Product
        {
            Name = $"Tracked Insert Product {i}",
            Price = 30.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(products);

        result.InsertedEntities.Count.ShouldBe(3);
        result.InsertedEntities.ShouldAllBe(e => e.Operation == UpsertOperationType.Insert);
        result.InsertedEntities.ShouldAllBe(e => e.Id > 0);
    }

    [Fact]
    public void UpsertBatch_Result_TracksUpdatedEntities()
    {
        using var context = CreateContext();
        SeedData(context, 3);

        var products = context.Products.ToList();
        foreach (var p in products)
            p.Stock += 50;

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(products);

        result.UpdatedEntities.Count.ShouldBe(3);
        result.UpdatedEntities.ShouldAllBe(e => e.Operation == UpsertOperationType.Update);
        result.UpdatedEntities.ShouldAllBe(e => e.Id > 0);
    }

    [Fact]
    public void UpsertBatch_Result_AllUpsertedEntities_InOrder()
    {
        using var context = CreateContext();
        SeedData(context, 2);

        var existingProducts = context.Products.ToList();
        foreach (var p in existingProducts)
            p.Price += 1.00m;

        var newProducts = Enumerable.Range(1, 3).Select(i => new Product
        {
            Name = $"Ordered Product {i}",
            Price = 40.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        context.ChangeTracker.Clear();

        var allEntities = existingProducts.Concat(newProducts).ToList();
        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(allEntities);

        var orderedIndexes = result.AllUpsertedEntities.Select(e => e.OriginalIndex).ToList();
        orderedIndexes.ShouldBe(orderedIndexes.OrderBy(x => x).ToList());
    }

    [Fact]
    public void UpsertBatch_Result_TracksDuration()
    {
        using var context = CreateContext();
        SeedData(context, 5);

        var products = context.Products.ToList();
        foreach (var p in products)
            p.Price += 1.00m;

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(products);

        result.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void UpsertBatch_Result_TracksRoundTrips()
    {
        using var context = CreateContext();
        SeedData(context, 5);

        var products = context.Products.ToList();
        foreach (var p in products)
            p.Price += 1.00m;

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(products);

        result.DatabaseRoundTrips.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void UpsertBatch_PartialFailure_TracksCorrectly()
    {
        using var context = CreateContext();
        SeedData(context, 3);

        var existingProducts = context.Products.ToList();
        existingProducts[0].Price += 5.00m; // Valid update
        existingProducts[1].Price = -10.00m; // Invalid update

        var newProducts = new[]
        {
            new Product { Name = "Valid New", Price = 20.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "Invalid New", Price = -5.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow }
        };

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(existingProducts.Concat(newProducts));

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3); // 2 valid updates + 1 valid insert
        result.FailureCount.ShouldBe(2); // 1 invalid update + 1 invalid insert
    }
}
