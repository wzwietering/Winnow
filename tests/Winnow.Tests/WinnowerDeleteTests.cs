using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

public class WinnowerDeleteTests : TestBase
{
    [Fact]
    public void Delete_SingleEntity_DeletesSuccessfully()
    {
        using var context = CreateContext();
        SeedData(context, 5);

        var productToDelete = context.Products.First();
        context.ChangeTracker.Clear();

        var saver = new Winnower<Product, int>(context);
        var result = saver.Delete([productToDelete]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);
        result.SuccessfulIds.ShouldContain(productToDelete.Id);

        context.ChangeTracker.Clear();
        context.Products.Find(productToDelete.Id).ShouldBeNull();
    }

    [Fact]
    public void Delete_MultipleEntities_AllDeleted()
    {
        using var context = CreateContext();
        SeedData(context, 10);

        var productsToDelete = context.Products.Take(5).ToList();
        var deletedIds = productsToDelete.Select(p => p.Id).ToList();
        context.ChangeTracker.Clear();

        var saver = new Winnower<Product, int>(context);
        var result = saver.Delete(productsToDelete);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(5);
        result.SuccessfulIds.Count.ShouldBe(5);

        context.ChangeTracker.Clear();
        var remainingProducts = context.Products.ToList();
        remainingProducts.Count.ShouldBe(5);
        remainingProducts.ShouldAllBe(p => !deletedIds.Contains(p.Id));
    }

    [Fact]
    public void Delete_EmptyCollection_ReturnsEmptyResult()
    {
        using var context = CreateContext();

        var saver = new Winnower<Product, int>(context);
        var result = saver.Delete([]);

        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
        result.TotalProcessed.ShouldBe(0);
    }

    [Fact]
    public void Delete_PartialFailure_SomeSucceedSomeFail()
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

        var saver = new Winnower<Product, int>(context);
        var result = saver.Delete([existingProduct, nonExistingProduct]);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);
        result.FailureCount.ShouldBe(1);
        result.SuccessfulIds.ShouldContain(existingProduct.Id);
        result.Failures[0].EntityId.ShouldBe(9999);
    }

    [Fact]
    public void Delete_NavigationValidation_ThrowsIfChildrenLoaded()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, 2);

        var orderWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First();
        context.ChangeTracker.Clear();

        var saver = new Winnower<CustomerOrder, int>(context);

        Should.Throw<InvalidOperationException>(() => saver.Delete([orderWithChildren]))
            .Message.ShouldContain("populated navigation properties");
    }

    [Fact]
    public void Delete_NavigationValidation_Disabled_AllowsDeletion()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, 2);

        var orderWithChildren = context.CustomerOrders
            .Include(o => o.OrderItems)
            .First();
        var orderId = orderWithChildren.Id;
        context.ChangeTracker.Clear();

        var saver = new Winnower<CustomerOrder, int>(context);
        var options = new DeleteOptions { ValidateNavigationProperties = false };

        var result = saver.Delete([orderWithChildren], options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        context.CustomerOrders.Find(orderId).ShouldBeNull();
    }

    [Fact]
    public void Delete_OneByOne_CorrectRoundTrips()
    {
        using var context = CreateContext();
        SeedData(context, 5);

        var productsToDelete = context.Products.ToList();
        context.ChangeTracker.Clear();

        var saver = new Winnower<Product, int>(context);
        var options = new DeleteOptions { Strategy = BatchStrategy.OneByOne };
        var result = saver.Delete(productsToDelete, options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.DatabaseRoundTrips.ShouldBe(5);
    }

    [Fact]
    public void Delete_DivideAndConquer_EfficientOnSuccess()
    {
        using var context = CreateContext();
        SeedData(context, 10);

        var productsToDelete = context.Products.ToList();
        context.ChangeTracker.Clear();

        var saver = new Winnower<Product, int>(context);
        var options = new DeleteOptions { Strategy = BatchStrategy.DivideAndConquer };
        var result = saver.Delete(productsToDelete, options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(10);
        result.DatabaseRoundTrips.ShouldBeLessThan(10);
    }

    [Fact]
    public void Delete_DivideAndConquer_IsolatesFailures()
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

        var saver = new Winnower<Product, int>(context);
        var options = new DeleteOptions { Strategy = BatchStrategy.DivideAndConquer };
        var result = saver.Delete(mixedProducts, options);

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(4);
        result.FailureCount.ShouldBe(1);
        result.Failures[0].EntityId.ShouldBe(9999);
    }

    [Fact]
    public void Delete_LargeDataSet_AllSucceed()
    {
        using var context = CreateContext();
        SeedData(context, 100);

        var productsToDelete = context.Products.ToList();
        context.ChangeTracker.Clear();

        var saver = new Winnower<Product, int>(context);
        var result = saver.Delete(productsToDelete);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(100);

        context.ChangeTracker.Clear();
        context.Products.Count().ShouldBe(0);
    }

    [Fact]
    public void Delete_SuccessRate_CalculatedCorrectly()
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

        var saver = new Winnower<Product, int>(context);
        var result = saver.Delete(existingProducts.Concat([nonExistingProduct]));

        result.SuccessRate.ShouldBe(0.75);
    }
}
