using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests;

/// <summary>
/// Tests for race condition handling and concurrency scenarios in upsert operations.
/// </summary>
public class BatchSaverUpsertConcurrencyTests : TestBase
{
    [Fact]
    public void UpsertBatch_ExistingEntityLoaded_PerformsUpdate()
    {
        // Verifies that entities loaded from DB and then upserted perform UPDATE
        using var context = CreateContext();
        SeedData(context, 1);

        // Load entity from database
        var existingProduct = context.Products.First();
        var productId = existingProduct.Id;
        existingProduct.Name = "Updated Product";
        existingProduct.Price = 100.00m;

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch([existingProduct]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(1);
        result.InsertedCount.ShouldBe(0);

        // Verify the update was applied
        context.ChangeTracker.Clear();
        var product = context.Products.Find(productId);
        product!.Name.ShouldBe("Updated Product");
        product.Price.ShouldBe(100.00m);
    }

    [Fact]
    public void UpsertBatch_MixedNewAndExisting_CorrectlyPartitions()
    {
        // Tests that a batch with both new and existing entities is handled correctly
        using var context = CreateContext();
        SeedData(context, 2);

        // Load existing products
        var existingProducts = context.Products.ToList();
        foreach (var p in existingProducts)
        {
            p.Price += 50.00m;
        }

        context.ChangeTracker.Clear();

        // Add new products to the mix
        var newProducts = Enumerable.Range(1, 2).Select(i => new Product
        {
            Name = $"New Product {i}",
            Price = 25.00m,
            Stock = 50,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var allProducts = existingProducts.Concat(newProducts).ToList();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(allProducts);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(2);
        result.InsertedCount.ShouldBe(2);

        context.ChangeTracker.Clear();
        context.Products.Count().ShouldBe(4);
    }

    [Fact]
    public async Task UpsertBatchAsync_SequentialInserts_AllSucceed()
    {
        // Test that sequential insert operations complete successfully
        using var context = CreateContext();

        var results = new List<UpsertBatchResult<int>>();

        // Run 5 sequential insert operations
        for (int batch = 0; batch < 5; batch++)
        {
            var products = Enumerable.Range(1, 3).Select(i => new Product
            {
                Name = $"Sequential Product {batch}-{i}",
                Price = 10.00m * (batch + 1),
                Stock = 100,
                LastModified = DateTimeOffset.UtcNow
            }).ToList();

            var saver = new BatchSaver<Product, int>(context);
            var result = await saver.UpsertBatchAsync(products);
            results.Add(result);

            context.ChangeTracker.Clear();
        }

        // All operations should complete successfully
        foreach (var result in results)
        {
            result.IsCompleteSuccess.ShouldBeTrue();
            result.InsertedCount.ShouldBe(3);
        }

        // Verify all products were inserted (15 total)
        context.Products.Count().ShouldBe(15);
    }

    [Fact]
    public async Task UpsertBatchAsync_SequentialUpdatesToSameEntity_AllSucceed()
    {
        // Test that sequential updates to the same entity all succeed
        // when we properly load the entity between updates
        using var context = CreateContext();
        SeedData(context, 1);

        var productId = context.Products.First().Id;
        context.ChangeTracker.Clear();

        var results = new List<UpsertBatchResult<int>>();

        // Run 3 sequential updates to the same product
        for (int i = 0; i < 3; i++)
        {
            // Load fresh from DB each time
            var product = context.Products.Find(productId)!;
            product.Name = $"Update {i + 1}";
            product.Price = 100.00m * (i + 1);
            product.Stock = 50;

            context.ChangeTracker.Clear();

            var saver = new BatchSaver<Product, int>(context);
            var result = await saver.UpsertBatchAsync([product]);
            results.Add(result);

            context.ChangeTracker.Clear();
        }

        // All updates should succeed
        foreach (var result in results)
        {
            result.IsCompleteSuccess.ShouldBeTrue();
            result.UpdatedCount.ShouldBe(1);
        }

        // Product should have the last price
        var finalProduct = context.Products.Find(productId);
        finalProduct!.Price.ShouldBe(300.00m);
        finalProduct.Name.ShouldBe("Update 3");
    }

    [Fact]
    public void UpsertBatch_WithConcurrencyToken_TrackedEntity_DetectsConflict()
    {
        // Test that concurrency tokens work when entity is tracked
        // and another process updates the database version
        using var context = CreateContext();
        SeedData(context, 1);

        // Load the product (tracked) - this captures the original Version
        var originalProduct = context.Products.First();
        var productId = originalProduct.Id;

        // Simulate another process updating the product AND its version directly in DB
        // SQLite doesn't auto-increment row versions, so we must do it explicitly
        // X'0100000000000000' is a different byte array than the default [0,0,0,0,0,0,0,0]
        context.Database.ExecuteSqlRaw(
            "UPDATE Products SET Price = 999.99, Version = X'0100000000000000' WHERE Id = {0}",
            productId);

        // Now try to upsert our entity which still has the old Version
        originalProduct.Name = "Stale Update";
        originalProduct.Price = 50.00m;

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch([originalProduct]);

        // Should fail with concurrency conflict because the Version in DB
        // differs from the Version we loaded
        result.IsCompleteSuccess.ShouldBeFalse();
        result.FailureCount.ShouldBe(1);
        result.Failures[0].Reason.ShouldBe(FailureReason.ConcurrencyConflict);
    }

    [Fact]
    public void UpsertGraphBatch_NewEntityWithChildren_InsertsSuccessfully()
    {
        // Test that graph operations work correctly with new entities
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-CONC-001",
            CustomerName = "Concurrency Customer",
            CustomerId = 9000,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 100.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = 9001,
                    ProductName = "Concurrency Product",
                    Quantity = 1,
                    UnitPrice = 100.00m,
                    Subtotal = 100.00m
                }
            ]
        };

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch([order]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);

        // Verify the child was also inserted
        context.ChangeTracker.Clear();
        var savedOrder = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First(o => o.OrderNumber == "ORD-CONC-001");
        savedOrder.OrderItems.Count.ShouldBe(1);
    }

    [Fact]
    public void UpsertBatch_LargeBatch_ProcessesSuccessfully()
    {
        // Test that large batches are processed without issues
        using var context = CreateContext();

        var products = Enumerable.Range(1, 100).Select(i => new Product
        {
            Name = $"Batch Product {i}",
            Price = 10.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(100);

        context.ChangeTracker.Clear();
        context.Products.Count().ShouldBe(100);
    }

    [Fact]
    public void UpsertBatch_EmptyBatch_ReturnsEmptyResult()
    {
        // Test handling of empty input
        using var context = CreateContext();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch([]);

        result.IsCompleteSuccess.ShouldBeFalse(); // No successes means not "complete success"
        result.InsertedCount.ShouldBe(0);
        result.UpdatedCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
    }

    [Fact]
    public async Task UpsertBatchAsync_WithCancellation_StopsProcessing()
    {
        // Test that cancellation is honored
        using var context = CreateContext();
        using var cts = new CancellationTokenSource();

        var products = Enumerable.Range(1, 10).Select(i => new Product
        {
            Name = $"Cancelled Product {i}",
            Price = 10.00m,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        // Cancel immediately
        cts.Cancel();

        var saver = new BatchSaver<Product, int>(context);
        var result = await saver.UpsertBatchAsync(products, cancellationToken: cts.Token);

        result.WasCancelled.ShouldBeTrue();
        result.TotalProcessed.ShouldBeLessThan(10);
    }
}
