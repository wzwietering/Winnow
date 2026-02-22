using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Shouldly;

namespace Winnow.Tests;

public class BatchSaverInsertTests : TestBase
{
    [Fact]
    public void InsertBatch_SingleEntity_ReturnsGeneratedId()
    {
        using var context = CreateContext();

        var product = new Product
        {
            Name = "New Product",
            Price = 25.00m,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        };

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.InsertBatch([product]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);
        result.InsertedEntities.Count.ShouldBe(1);
        result.InsertedEntities[0].Id.ShouldBeGreaterThan(0);
        result.InsertedEntities[0].OriginalIndex.ShouldBe(0);
        product.Id.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void InsertBatch_MultipleEntities_AllInsertedWithIds()
    {
        using var context = CreateContext();

        var products = Enumerable.Range(1, 5).Select(i => new Product
        {
            Name = $"Product {i}",
            Price = 10.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.InsertBatch(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(5);
        result.InsertedIds.Count.ShouldBe(5);
        result.InsertedIds.ShouldAllBe(id => id > 0);

        for (int i = 0; i < products.Count; i++)
        {
            var inserted = result.InsertedEntities.First(e => e.OriginalIndex == i);
            inserted.Id.ShouldBeGreaterThan(0);
        }
    }

    [Fact]
    public void InsertBatch_EmptyCollection_ReturnsEmptyResult()
    {
        using var context = CreateContext();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.InsertBatch([]);

        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
        result.TotalProcessed.ShouldBe(0);
    }

    [Fact]
    public void InsertBatch_ValidationError_TracksFailureWithIndex()
    {
        using var context = CreateContext();

        var products = new[]
        {
            new Product { Name = "Valid", Price = 10.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "Invalid", Price = -5.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "Valid 2", Price = 20.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow }
        };

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.InsertBatch(products);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(2);
        result.FailureCount.ShouldBe(1);
        result.Failures[0].EntityIndex.ShouldBe(1);
        result.Failures[0].Reason.ShouldBe(FailureReason.ValidationError);
    }

    [Fact]
    public void InsertBatch_PartialFailure_CorrectIndicesTracked()
    {
        using var context = CreateContext();

        var products = new[]
        {
            new Product { Name = "P1", Price = 10.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "P2", Price = -5.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "P3", Price = 15.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "P4", Price = -10.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "P5", Price = 20.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow }
        };

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.InsertBatch(products);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.FailureCount.ShouldBe(2);

        var failedIndices = result.Failures.Select(f => f.EntityIndex).OrderBy(x => x).ToList();
        failedIndices.ShouldBe([1, 3]);

        var successIndices = result.InsertedEntities.Select(e => e.OriginalIndex).OrderBy(x => x).ToList();
        successIndices.ShouldBe([0, 2, 4]);
    }

    [Fact]
    public void InsertBatch_LargeBatch_PerformanceTest()
    {
        using var context = CreateContext();

        var products = Enumerable.Range(1, 100).Select(i => new Product
        {
            Name = $"Product {i}",
            Price = 10.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.InsertBatch(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(100);
        result.DatabaseRoundTrips.ShouldBe(100);
    }

    [Fact]
    public void InsertBatch_NavigationValidation_ThrowsIfChildrenPopulated()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-001",
            CustomerName = "Test Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 100.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = 1,
                    ProductName = "Test Product",
                    Quantity = 2,
                    UnitPrice = 50.00m,
                    Subtotal = 100.00m
                }
            ]
        };

        var saver = new BatchSaver<CustomerOrder, int>(context);

        Should.Throw<InvalidOperationException>(() => saver.InsertBatch([order]))
            .Message.ShouldContain("populated navigation properties");
    }

    [Fact]
    public void InsertBatch_NavigationValidation_Disabled_AllowsPopulated()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-001",
            CustomerName = "Test Customer",
            CustomerId = 1,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 100.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = 1,
                    ProductName = "Test Product",
                    Quantity = 2,
                    UnitPrice = 50.00m,
                    Subtotal = 100.00m
                }
            ]
        };

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var options = new InsertBatchOptions { ValidateNavigationProperties = false };

        var result = saver.InsertBatch([order], options);

        result.SuccessCount.ShouldBe(1);
    }

    [Fact]
    public void InsertBatch_OneByOne_CorrectRoundTrips()
    {
        using var context = CreateContext();

        var products = Enumerable.Range(1, 5).Select(i => new Product
        {
            Name = $"Product {i}",
            Price = 10.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.InsertBatch(products, new InsertBatchOptions { Strategy = BatchStrategy.OneByOne });

        result.DatabaseRoundTrips.ShouldBe(5);
    }

    [Fact]
    public void InsertBatch_DivideAndConquer_EfficientOnSuccess()
    {
        using var context = CreateContext();

        var products = Enumerable.Range(1, 10).Select(i => new Product
        {
            Name = $"Product {i}",
            Price = 10.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.InsertBatch(products, new InsertBatchOptions { Strategy = BatchStrategy.DivideAndConquer });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(10);
        result.DatabaseRoundTrips.ShouldBeLessThan(10);
    }

    [Fact]
    public void InsertBatch_DivideAndConquer_IsolatesFailures()
    {
        using var context = CreateContext();

        var products = new[]
        {
            new Product { Name = "P1", Price = 10.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "P2", Price = 15.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "P3", Price = -5.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "P4", Price = 20.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow }
        };

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.InsertBatch(products, new InsertBatchOptions { Strategy = BatchStrategy.DivideAndConquer });

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.FailureCount.ShouldBe(1);
        result.Failures[0].EntityIndex.ShouldBe(2);
    }

    [Fact]
    public void InsertBatch_CompleteFailure_AllFailed()
    {
        using var context = CreateContext();

        var products = new[]
        {
            new Product { Name = "P1", Price = -10.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "P2", Price = -5.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow }
        };

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.InsertBatch(products);

        result.IsCompleteFailure.ShouldBeTrue();
        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(2);
    }

    [Fact]
    public void InsertBatch_SuccessRate_CalculatedCorrectly()
    {
        using var context = CreateContext();

        var products = new[]
        {
            new Product { Name = "P1", Price = 10.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "P2", Price = -5.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "P3", Price = 15.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "P4", Price = 20.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow }
        };

        var saver = new BatchSaver<Product, int>(context);
        var result = saver.InsertBatch(products);

        result.SuccessRate.ShouldBe(0.75);
    }
}
