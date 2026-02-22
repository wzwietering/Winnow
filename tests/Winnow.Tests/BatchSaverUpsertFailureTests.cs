using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

public class BatchSaverUpsertFailureTests : TestBase
{
    [Fact]
    public void UpsertBatch_ValidationError_TrackedAsFailure()
    {
        using var context = CreateContext();

        var products = new[]
        {
            new Product { Name = "Valid", Price = 10.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "Invalid", Price = -5.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow }
        };

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(products);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);
        result.FailureCount.ShouldBe(1);
        result.Failures[0].Reason.ShouldBe(FailureReason.ValidationError);
    }

    [Fact]
    public void UpsertBatch_ConcurrencyConflict_TrackedAsFailure()
    {
        using var context = CreateContext();
        SeedData(context, 1);

        var product = context.Products.First();
        var originalVersion = product.Version.ToArray();
        product.Price += 10.00m;

        // Simulate concurrent update by directly modifying the version
        context.Database.ExecuteSqlRaw(
            "UPDATE Products SET Price = Price + 5, Version = X'0102030405060708' WHERE Id = {0}",
            product.Id);

        // Update the product's version to old value to simulate stale data
        product.Version = originalVersion;

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch([product]);

        result.FailureCount.ShouldBe(1);
        result.Failures[0].Reason.ShouldBe(FailureReason.ConcurrencyConflict);
    }

    [Fact]
    public void UpsertBatch_FKViolation_TrackedAsFailure()
    {
        using var context = CreateContext();

        // Product with non-existent category
        var product = new Product
        {
            Name = "FK Violation Product",
            Price = 25.00m,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow,
            CategoryId = 99999 // Non-existent
        };

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch([product]);

        result.FailureCount.ShouldBe(1);
        result.Failures[0].Reason.ShouldBe(FailureReason.DatabaseConstraint);
    }

    [Fact]
    public void UpsertBatch_UniqueConstraint_TrackedAsFailure()
    {
        using var context = CreateContext();

        var order1 = new CustomerOrder
        {
            OrderNumber = "ORD-UNIQUE-001",
            CustomerName = "Customer 1",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 100.00m,
            OrderDate = DateTimeOffset.UtcNow
        };
        context.CustomerOrders.Add(order1);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        // Try to insert with duplicate order number
        var order2 = new CustomerOrder
        {
            OrderNumber = "ORD-UNIQUE-001", // Duplicate
            CustomerName = "Customer 2",
            CustomerId = 2,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 200.00m,
            OrderDate = DateTimeOffset.UtcNow
        };

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertBatch([order2]);

        result.FailureCount.ShouldBe(1);
        result.Failures[0].Reason.ShouldBe(FailureReason.DuplicateKey);
    }

    [Fact]
    public void UpsertBatch_Failure_HasAttemptedOperation()
    {
        using var context = CreateContext();
        SeedData(context, 2);

        var existingProduct = context.Products.First();
        existingProduct.Price = -10.00m; // Invalid update

        var newProduct = new Product
        {
            Name = "Invalid New",
            Price = -5.00m, // Invalid insert
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        };

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch([existingProduct, newProduct]);

        result.FailureCount.ShouldBe(2);
        result.Failures.ShouldAllBe(f =>
            f.AttemptedOperation == UpsertOperationType.Insert ||
            f.AttemptedOperation == UpsertOperationType.Update);
    }

    [Fact]
    public void UpsertBatch_InsertFailure_AttemptedOperationIsInsert()
    {
        using var context = CreateContext();

        var product = new Product
        {
            Id = 0,
            Name = "Insert Failure",
            Price = -5.00m, // Invalid
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        };

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch([product]);

        result.FailureCount.ShouldBe(1);
        result.Failures[0].AttemptedOperation.ShouldBe(UpsertOperationType.Insert);
    }

    [Fact]
    public void UpsertBatch_UpdateFailure_AttemptedOperationIsUpdate()
    {
        using var context = CreateContext();
        SeedData(context, 1);

        var product = context.Products.First();
        product.Price = -10.00m; // Invalid

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch([product]);

        result.FailureCount.ShouldBe(1);
        result.Failures[0].AttemptedOperation.ShouldBe(UpsertOperationType.Update);
    }

    [Fact]
    public void UpsertBatch_Failure_HasEntityIndex()
    {
        using var context = CreateContext();

        var products = new[]
        {
            new Product { Name = "P0", Price = 10.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "P1", Price = -5.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "P2", Price = 15.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "P3", Price = -10.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow }
        };

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(products);

        result.FailureCount.ShouldBe(2);
        var failedIndexes = result.Failures.Select(f => f.EntityIndex).OrderBy(x => x).ToList();
        failedIndexes.ShouldBe([1, 3]);
    }

    [Fact]
    public void UpsertBatch_Failure_HasEntityId_WhenKnown()
    {
        using var context = CreateContext();
        SeedData(context, 3);

        var products = context.Products.ToList();
        products[1].Price = -10.00m; // Make one invalid

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch(products);

        result.FailureCount.ShouldBe(1);
        result.Failures[0].EntityId.ShouldNotBe(default);
        result.Failures[0].EntityId.ShouldBe(products[1].Id);
    }

    [Fact]
    public void UpsertBatch_UpdateNonExistent_Fails()
    {
        using var context = CreateContext();

        var product = new Product
        {
            Id = 99999, // Non-existent
            Name = "Non-existent Product",
            Price = 25.00m,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        };

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.UpsertBatch([product]);

        result.FailureCount.ShouldBe(1);
        result.Failures[0].AttemptedOperation.ShouldBe(UpsertOperationType.Update);
    }

    [Fact]
    public void UpsertBatch_InsertDuplicate_Fails()
    {
        using var context = CreateContext();

        var order1 = new CustomerOrder
        {
            OrderNumber = "ORD-DUP-001",
            CustomerName = "Customer 1",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 100.00m,
            OrderDate = DateTimeOffset.UtcNow
        };
        context.CustomerOrders.Add(order1);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        // Try to insert duplicate
        var duplicateOrder = new CustomerOrder
        {
            Id = 0, // New entity
            OrderNumber = "ORD-DUP-001", // Duplicate unique constraint
            CustomerName = "Customer 2",
            CustomerId = 2,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 200.00m,
            OrderDate = DateTimeOffset.UtcNow
        };

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertBatch([duplicateOrder]);

        result.IsCompleteFailure.ShouldBeTrue();
        result.FailureCount.ShouldBe(1);
        result.Failures[0].AttemptedOperation.ShouldBe(UpsertOperationType.Insert);
    }

    [Fact]
    public void UpsertGraphBatch_ChildFailure_FailsEntireGraph()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 2);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();

        // Make child invalid in first order
        orders[0].Status = CustomerOrderStatus.Completed;
        orders[0].OrderItems.First().Quantity = -1; // Invalid

        // Keep other orders valid
        orders[1].Status = CustomerOrderStatus.Processing;
        orders[2].Status = CustomerOrderStatus.Cancelled;

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = saver.UpsertGraphBatch(orders);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(2);
        result.FailureCount.ShouldBe(1);
        result.Failures[0].EntityId.ShouldBe(orders[0].Id);

        // Verify first order was not updated (rolled back)
        context.ChangeTracker.Clear();
        var verifyOrder = context.CustomerOrders.Find(orders[0].Id);
        verifyOrder!.Status.ShouldNotBe(CustomerOrderStatus.Completed);
    }
}
