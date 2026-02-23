using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

/// <summary>
/// Tests for race condition handling and concurrency scenarios in upsert operations.
/// </summary>
public class WinnowerUpsertConcurrencyTests : TestBase
{
    [Fact]
    public void Upsert_ExistingEntityLoaded_PerformsUpdate()
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

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert([existingProduct]);

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
    public void Upsert_MixedNewAndExisting_CorrectlyPartitions()
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

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert(allProducts);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(2);
        result.InsertedCount.ShouldBe(2);

        context.ChangeTracker.Clear();
        context.Products.Count().ShouldBe(4);
    }

    [Fact]
    public async Task UpsertAsync_SequentialInserts_AllSucceed()
    {
        // Test that sequential insert operations complete successfully
        using var context = CreateContext();

        var results = new List<UpsertResult<int>>();

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

            var saver = new Winnower<Product, int>(context);
            var result = await saver.UpsertAsync(products);
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
    public async Task UpsertAsync_SequentialUpdatesToSameEntity_AllSucceed()
    {
        // Test that sequential updates to the same entity all succeed
        // when we properly load the entity between updates
        using var context = CreateContext();
        SeedData(context, 1);

        var productId = context.Products.First().Id;
        context.ChangeTracker.Clear();

        var results = new List<UpsertResult<int>>();

        // Run 3 sequential updates to the same product
        for (int i = 0; i < 3; i++)
        {
            // Load fresh from DB each time
            var product = context.Products.Find(productId)!;
            product.Name = $"Update {i + 1}";
            product.Price = 100.00m * (i + 1);
            product.Stock = 50;

            context.ChangeTracker.Clear();

            var saver = new Winnower<Product, int>(context);
            var result = await saver.UpsertAsync([product]);
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
    public void Upsert_WithConcurrencyToken_TrackedEntity_DetectsConflict()
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

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert([originalProduct]);

        // Should fail with concurrency conflict because the Version in DB
        // differs from the Version we loaded
        result.IsCompleteSuccess.ShouldBeFalse();
        result.FailureCount.ShouldBe(1);
        result.Failures[0].Reason.ShouldBe(FailureReason.ConcurrencyConflict);
    }

    [Fact]
    public void UpsertGraph_NewEntityWithChildren_InsertsSuccessfully()
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

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpsertGraph([order]);

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
    public void Upsert_LargeBatch_ProcessesSuccessfully()
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

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(100);

        context.ChangeTracker.Clear();
        context.Products.Count().ShouldBe(100);
    }

    [Fact]
    public void Upsert_EmptyBatch_ReturnsEmptyResult()
    {
        // Test handling of empty input
        using var context = CreateContext();

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert([]);

        result.IsCompleteSuccess.ShouldBeFalse(); // No successes means not "complete success"
        result.InsertedCount.ShouldBe(0);
        result.UpdatedCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
    }

    [Fact]
    public async Task UpsertAsync_WithCancellation_StopsProcessing()
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

        var saver = new Winnower<Product, int>(context);
        var result = await saver.UpsertAsync(products, cancellationToken: cts.Token);

        result.WasCancelled.ShouldBeTrue();
        result.TotalProcessed.ShouldBeLessThan(10);
    }

    [Fact]
    public void Upsert_DuplicateKey_IsDefaultKeySetCorrectly()
    {
        // Verifies that IsDefaultKey is true when INSERT attempt fails with duplicate key
        using var context = CreateContext();

        // Insert existing order
        var existingOrder = new CustomerOrder
        {
            OrderNumber = "ORD-DUP-001",
            CustomerName = "First Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 100.00m,
            OrderDate = DateTimeOffset.UtcNow
        };
        context.CustomerOrders.Add(existingOrder);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        // Try to insert new order with duplicate OrderNumber (Id = 0 → INSERT attempt)
        var duplicateOrder = new CustomerOrder
        {
            OrderNumber = "ORD-DUP-001", // Duplicate unique constraint
            CustomerName = "Second Customer",
            CustomerId = 2,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 200.00m,
            OrderDate = DateTimeOffset.UtcNow
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.Upsert([duplicateOrder]);

        result.FailureCount.ShouldBe(1);
        result.Failures[0].Reason.ShouldBe(FailureReason.DuplicateKey);
        result.Failures[0].IsDefaultKey.ShouldBeTrue();
        result.Failures[0].AttemptedOperation.ShouldBe(UpsertOperationType.Insert);
        result.Failures[0].EntityId.ShouldBe(0); // Default for int key
    }

    [Fact]
    public async Task UpsertAsync_DuplicateKey_RetryAsUpdate_RetriesSuccessfully()
    {
        // Tests that DuplicateKeyStrategy.RetryAsUpdate handles the scenario
        // where an UPDATE uses the existing entity with proper concurrency token
        using var context = CreateContext();

        // Insert existing order
        var existingOrder = new CustomerOrder
        {
            OrderNumber = "ORD-RETRY-001",
            CustomerName = "Original Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 100.00m,
            OrderDate = DateTimeOffset.UtcNow
        };
        context.CustomerOrders.Add(existingOrder);
        await context.SaveChangesAsync();
        var existingId = existingOrder.Id;
        var existingVersion = existingOrder.Version;
        context.ChangeTracker.Clear();

        // Create entity with the existing ID and Version for proper update
        var updatingOrder = new CustomerOrder
        {
            Id = existingId, // Non-default ID → treated as UPDATE
            OrderNumber = "ORD-RETRY-001",
            CustomerName = "Updated Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Completed,
            TotalAmount = 150.00m,
            OrderDate = DateTimeOffset.UtcNow,
            Version = existingVersion // Must match for concurrency check
        };

        var options = new UpsertOptions { DuplicateKeyStrategy = DuplicateKeyStrategy.RetryAsUpdate };
        var saver = new Winnower<CustomerOrder, int>(context);
        var result = await saver.UpsertAsync([updatingOrder], options);

        // Should succeed as UPDATE
        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(1);

        // Verify changes were persisted
        context.ChangeTracker.Clear();
        var savedOrder = context.CustomerOrders.Find(existingId);
        savedOrder!.CustomerName.ShouldBe("Updated Customer");
        savedOrder.TotalAmount.ShouldBe(150.00m);
    }

    [Fact]
    public async Task UpsertAsync_DuplicateKey_Skip_DoesNotRecordFailure()
    {
        // Tests that DuplicateKeyStrategy.Skip silently skips duplicate key errors
        using var context = CreateContext();

        // Insert existing order
        var existingOrder = new CustomerOrder
        {
            OrderNumber = "ORD-SKIP-001",
            CustomerName = "Existing Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 100.00m,
            OrderDate = DateTimeOffset.UtcNow
        };
        context.CustomerOrders.Add(existingOrder);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Try to insert with duplicate order number
        var duplicateOrder = new CustomerOrder
        {
            OrderNumber = "ORD-SKIP-001", // Duplicate
            CustomerName = "New Customer",
            CustomerId = 2,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 200.00m,
            OrderDate = DateTimeOffset.UtcNow
        };

        // Add a valid order to verify partial success
        var validOrder = new CustomerOrder
        {
            OrderNumber = "ORD-SKIP-002",
            CustomerName = "Valid Customer",
            CustomerId = 3,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 300.00m,
            OrderDate = DateTimeOffset.UtcNow
        };

        var options = new UpsertOptions
        {
            DuplicateKeyStrategy = DuplicateKeyStrategy.Skip,
            Strategy = BatchStrategy.OneByOne // Ensure entity isolation
        };
        var saver = new Winnower<CustomerOrder, int>(context);
        var result = await saver.UpsertAsync([duplicateOrder, validOrder], options);

        // Duplicate should be skipped (not recorded as failure), valid should succeed
        result.FailureCount.ShouldBe(0);
        result.InsertedCount.ShouldBe(1);
        result.InsertedEntities[0].Entity.ShouldBe(validOrder);
    }

    [Fact]
    public void Upsert_UpdateFailure_IsDefaultKeyIsFalse()
    {
        // Verifies that IsDefaultKey is false when UPDATE attempt fails
        using var context = CreateContext();
        SeedData(context, 1);

        // Load existing product and make invalid changes
        var existingProduct = context.Products.First();
        var productId = existingProduct.Id;
        existingProduct.Price = -10.00m; // Invalid - will fail validation
        context.ChangeTracker.Clear();

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert([existingProduct]);

        result.FailureCount.ShouldBe(1);
        result.Failures[0].IsDefaultKey.ShouldBeFalse();
        result.Failures[0].AttemptedOperation.ShouldBe(UpsertOperationType.Update);
        result.Failures[0].EntityId.ShouldBe(productId);
    }
}
