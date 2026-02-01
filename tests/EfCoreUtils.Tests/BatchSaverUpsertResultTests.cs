using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Shouldly;

namespace EfCoreUtils.Tests;

public class BatchSaverUpsertResultTests : TestBase
{
    [Fact]
    public void UpsertBatchResult_InsertedIds_DoNotOverlapUpdatedIds()
    {
        using var context = CreateContext();
        SeedData(context, 5);

        var existingProducts = context.Products.Take(3).ToList();
        foreach (var p in existingProducts)
            p.Price += 1.00m;

        var newProducts = Enumerable.Range(1, 2).Select(i => new Product
        {
            Name = $"New Product {i}",
            Price = 25.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(existingProducts.Concat(newProducts));

        result.InsertedIds.Intersect(result.UpdatedIds).ShouldBeEmpty();
        result.InsertedCount.ShouldBe(2);
        result.UpdatedCount.ShouldBe(3);
    }

    [Fact]
    public void UpsertBatchResult_AllEntities_InSuccessOrFailure()
    {
        using var context = CreateContext();
        SeedData(context, 5);

        var existingProducts = context.Products.Take(2).ToList();
        existingProducts[0].Price += 1.00m;
        existingProducts[1].Price = -10.00m; // Invalid - will fail

        var newProducts = new[]
        {
            new Product { Name = "Valid New", Price = 20.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "Invalid New", Price = -5.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow }
        };

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var allEntities = existingProducts.Concat(newProducts).ToList();
        var result = saver.UpsertBatch(allEntities);

        var totalTracked = result.SuccessCount + result.FailureCount;
        totalTracked.ShouldBe(allEntities.Count);
    }

    [Fact]
    public void UpsertBatchResult_OriginalIndexes_Unique()
    {
        using var context = CreateContext();
        SeedData(context, 5);

        var products = context.Products.Take(3).ToList();
        foreach (var p in products)
            p.Price += 1.00m;

        var newProducts = Enumerable.Range(1, 3).Select(i => new Product
        {
            Name = $"New Product {i}",
            Price = 25.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(products.Concat(newProducts));

        var allIndexes = result.AllUpsertedEntities.Select(e => e.OriginalIndex).ToList();
        allIndexes.Distinct().Count().ShouldBe(allIndexes.Count);
    }

    [Fact]
    public void UpsertBatchResult_OperationProperty_Accurate()
    {
        using var context = CreateContext();
        SeedData(context, 3);

        var existingProduct = context.Products.First();
        existingProduct.Price += 5.00m;

        var newProduct = new Product
        {
            Name = "Brand New Product",
            Price = 30.00m,
            Stock = 50,
            LastModified = DateTimeOffset.UtcNow
        };

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch([existingProduct, newProduct]);

        result.IsCompleteSuccess.ShouldBeTrue();

        var inserted = result.InsertedEntities.Single();
        var updated = result.UpdatedEntities.Single();

        inserted.Operation.ShouldBe(UpsertOperationType.Insert);
        updated.Operation.ShouldBe(UpsertOperationType.Update);
    }

    [Fact]
    public void UpsertBatchResult_EntityReference_Preserved()
    {
        using var context = CreateContext();
        SeedData(context, 3);

        var existingProduct = context.Products.First();
        existingProduct.Price += 5.00m;

        var newProduct = new Product
        {
            Name = "Reference Test Product",
            Price = 40.00m,
            Stock = 75,
            LastModified = DateTimeOffset.UtcNow
        };

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch([existingProduct, newProduct]);

        result.IsCompleteSuccess.ShouldBeTrue();

        var insertedEntity = result.InsertedEntities.Single();
        var updatedEntity = result.UpdatedEntities.Single();

        insertedEntity.Entity.ShouldBeSameAs(newProduct);
        updatedEntity.Entity.ShouldBeSameAs(existingProduct);
    }
}
