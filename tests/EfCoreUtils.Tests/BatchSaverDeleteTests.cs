using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests;

public class BatchSaverDeleteTests : TestBase
{
    [Fact]
    public void DeleteBatch_SingleEntity_DeletesSuccessfully()
    {
        using var context = CreateContext();
        SeedData(context, 5);

        var productToDelete = context.Products.First();
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product>(context);
        var result = saver.DeleteBatch([productToDelete]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);
        result.SuccessfulIds.ShouldContain(productToDelete.Id);

        context.ChangeTracker.Clear();
        context.Products.Find(productToDelete.Id).ShouldBeNull();
    }

    [Fact]
    public void DeleteBatch_MultipleEntities_AllDeleted()
    {
        using var context = CreateContext();
        SeedData(context, 10);

        var productsToDelete = context.Products.Take(5).ToList();
        var deletedIds = productsToDelete.Select(p => p.Id).ToList();
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product>(context);
        var result = saver.DeleteBatch(productsToDelete);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(5);
        result.SuccessfulIds.Count.ShouldBe(5);

        context.ChangeTracker.Clear();
        var remainingProducts = context.Products.ToList();
        remainingProducts.Count.ShouldBe(5);
        remainingProducts.ShouldAllBe(p => !deletedIds.Contains(p.Id));
    }

    [Fact]
    public void DeleteBatch_EmptyCollection_ReturnsEmptyResult()
    {
        using var context = CreateContext();

        var saver = new BatchSaver<Product>(context);
        var result = saver.DeleteBatch([]);

        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
        result.TotalProcessed.ShouldBe(0);
    }

    [Fact]
    public void DeleteBatch_PartialFailure_SomeSucceedSomeFail()
    {
        using var context = CreateContext();
        SeedData(context, 5);

        var existingProduct = context.Products.First();
        var nonExistingProduct = new Product
        {
            Id = 9999,
            Name = "Fake",
            Price = 10.00m,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow,
            Version = new byte[8]
        };
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product>(context);
        var result = saver.DeleteBatch([existingProduct, nonExistingProduct]);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);
        result.FailureCount.ShouldBe(1);
        result.SuccessfulIds.ShouldContain(existingProduct.Id);
        result.Failures[0].EntityId.ShouldBe(9999);
    }

    [Fact]
    public void DeleteBatch_NavigationValidation_ThrowsIfChildrenLoaded()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, 2);

        var orderWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First();
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder>(context);

        Should.Throw<InvalidOperationException>(() => saver.DeleteBatch([orderWithChildren]))
            .Message.ShouldContain("populated navigation properties");
    }

    [Fact]
    public void DeleteBatch_NavigationValidation_Disabled_AllowsDeletion()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, 2);

        var orderWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First();
        var orderId = orderWithChildren.Id;
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder>(context);
        var options = new DeleteBatchOptions { ValidateNavigationProperties = false };

        var result = saver.DeleteBatch([orderWithChildren], options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        context.CustomerOrders.Find(orderId).ShouldBeNull();
    }

    [Fact]
    public void DeleteBatch_OneByOne_CorrectRoundTrips()
    {
        using var context = CreateContext();
        SeedData(context, 5);

        var productsToDelete = context.Products.ToList();
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product>(context);
        var options = new DeleteBatchOptions { Strategy = BatchStrategy.OneByOne };
        var result = saver.DeleteBatch(productsToDelete, options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.DatabaseRoundTrips.ShouldBe(5);
    }

    [Fact]
    public void DeleteBatch_DivideAndConquer_EfficientOnSuccess()
    {
        using var context = CreateContext();
        SeedData(context, 10);

        var productsToDelete = context.Products.ToList();
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product>(context);
        var options = new DeleteBatchOptions { Strategy = BatchStrategy.DivideAndConquer };
        var result = saver.DeleteBatch(productsToDelete, options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(10);
        result.DatabaseRoundTrips.ShouldBeLessThan(10);
    }

    [Fact]
    public void DeleteBatch_DivideAndConquer_IsolatesFailures()
    {
        using var context = CreateContext();
        SeedData(context, 4);

        var existingProducts = context.Products.ToList();
        var nonExistingProduct = new Product
        {
            Id = 9999,
            Name = "Fake",
            Price = 10.00m,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow,
            Version = new byte[8]
        };
        context.ChangeTracker.Clear();

        var mixedProducts = existingProducts.Take(2)
            .Concat([nonExistingProduct])
            .Concat(existingProducts.Skip(2))
            .ToList();

        var saver = new BatchSaver<Product>(context);
        var options = new DeleteBatchOptions { Strategy = BatchStrategy.DivideAndConquer };
        var result = saver.DeleteBatch(mixedProducts, options);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
        result.FailureCount.ShouldBe(1);
        result.Failures[0].EntityId.ShouldBe(9999);
    }

    [Fact]
    public void DeleteBatch_LargeBatch_PerformanceTest()
    {
        using var context = CreateContext();
        SeedData(context, 100);

        var productsToDelete = context.Products.ToList();
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product>(context);
        var result = saver.DeleteBatch(productsToDelete);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(100);

        context.ChangeTracker.Clear();
        context.Products.Count().ShouldBe(0);
    }

    [Fact]
    public void DeleteBatch_SuccessRate_CalculatedCorrectly()
    {
        using var context = CreateContext();
        SeedData(context, 3);

        var existingProducts = context.Products.ToList();
        var nonExistingProduct = new Product
        {
            Id = 9999,
            Name = "Fake",
            Price = 10.00m,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow,
            Version = new byte[8]
        };
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product>(context);
        var result = saver.DeleteBatch(existingProducts.Concat([nonExistingProduct]));

        result.SuccessRate.ShouldBe(0.75);
    }
}
