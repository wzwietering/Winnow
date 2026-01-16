using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Shouldly;

namespace EfCoreUtils.Tests;

public class GenericKeyTypeTests : TestBase
{
    [Fact]
    public void InsertBatch_WithIntKey_GeneratesKeys()
    {
        using var context = CreateContext();

        var products = Enumerable.Range(1, 3).Select(i => new Product
        {
            Name = $"Int Product {i}",
            Price = 10.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.InsertBatch(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.InsertedIds.ShouldAllBe(id => id > 0);
    }

    [Fact]
    public void InsertBatch_WithLongKey_GeneratesKeys()
    {
        using var context = CreateContext();

        var products = Enumerable.Range(1, 3).Select(i => new ProductLong
        {
            Name = $"Long Product {i}",
            Price = 10.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var saver = new BatchSaver<ProductLong, long>(context);
        var result = saver.InsertBatch(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.InsertedIds.ShouldAllBe(id => id > 0);
    }

    [Fact]
    public void InsertBatch_WithGuidKey_GeneratesKeys()
    {
        using var context = CreateContext();

        var products = Enumerable.Range(1, 3).Select(i => new ProductGuid
        {
            Id = Guid.NewGuid(),
            Name = $"Guid Product {i}",
            Price = 10.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var saver = new BatchSaver<ProductGuid, Guid>(context);
        var result = saver.InsertBatch(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.InsertedIds.ShouldAllBe(id => id != Guid.Empty);
    }

    [Fact]
    public void InsertBatch_WithStringKey_UsesProvidedKeys()
    {
        using var context = CreateContext();

        var products = Enumerable.Range(1, 3).Select(i => new ProductString
        {
            Id = $"PROD-{i:D4}",
            Name = $"String Product {i}",
            Price = 10.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var saver = new BatchSaver<ProductString, string>(context);
        var result = saver.InsertBatch(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.InsertedIds.ShouldContain("PROD-0001");
        result.InsertedIds.ShouldContain("PROD-0002");
        result.InsertedIds.ShouldContain("PROD-0003");
    }

    [Fact]
    public void UpdateBatch_WithGuidKey_TracksSuccessfulIds()
    {
        using var context = CreateContext();

        // Insert some products first
        var products = Enumerable.Range(1, 3).Select(i => new ProductGuid
        {
            Id = Guid.NewGuid(),
            Name = $"Guid Product {i}",
            Price = 10.00m,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        context.ProductGuids.AddRange(products);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        // Reload and update
        var productsToUpdate = context.ProductGuids.ToList();
        var expectedIds = productsToUpdate.Select(p => p.Id).ToList();
        foreach (var product in productsToUpdate)
        {
            product.Price += 5.00m;
        }

        var saver = new BatchSaver<ProductGuid, Guid>(context);
        var result = saver.UpdateBatch(productsToUpdate);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        foreach (var expectedId in expectedIds)
        {
            result.SuccessfulIds.ShouldContain(expectedId);
        }
    }

    [Fact]
    public void DeleteBatch_WithLongKey_TracksDeletedIds()
    {
        using var context = CreateContext();

        // Insert some products first
        var products = Enumerable.Range(1, 3).Select(i => new ProductLong
        {
            Name = $"Long Product {i}",
            Price = 10.00m,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        context.ProductLongs.AddRange(products);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        // Reload and delete
        var productsToDelete = context.ProductLongs.ToList();
        var expectedIds = productsToDelete.Select(p => p.Id).ToList();

        var saver = new BatchSaver<ProductLong, long>(context);
        var result = saver.DeleteBatch(productsToDelete);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        foreach (var expectedId in expectedIds)
        {
            result.SuccessfulIds.ShouldContain(expectedId);
        }

        // Verify deleted
        context.ChangeTracker.Clear();
        context.ProductLongs.Count().ShouldBe(0);
    }

    [Fact]
    public void BatchSaver_WithWrongKeyType_ThrowsDescriptiveError()
    {
        using var context = CreateContext();

        // Insert a product with int key
        var product = new Product
        {
            Name = "Int Product",
            Price = 10.00m,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        };
        context.Products.Add(product);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var productToUpdate = context.Products.First();
        productToUpdate.Price += 5.00m;

        // Try to use BatchSaver<Product, long> instead of BatchSaver<Product, int>
        var saver = new BatchSaver<Product, long>(context);
        var ex = Should.Throw<InvalidOperationException>(() => saver.UpdateBatch([productToUpdate]));

        ex.Message.ShouldContain("Primary key type mismatch");
        ex.Message.ShouldContain("Product");
        ex.Message.ShouldContain("Int64"); // long
        ex.Message.ShouldContain("Int32"); // int
    }
}
