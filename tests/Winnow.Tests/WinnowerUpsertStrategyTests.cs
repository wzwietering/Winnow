using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

public class WinnowerUpsertStrategyTests : TestBase
{
    [Fact]
    public void Upsert_OneByOne_IsolatesFailures()
    {
        using var context = CreateContext();
        SeedData(context, 5);

        var existingProducts = context.Products.Take(3).ToList();
        existingProducts[0].Price += 5.00m;
        existingProducts[1].Price = -10.00m; // Invalid
        existingProducts[2].Price += 3.00m;

        var newProducts = new[]
        {
            new Product { Name = "Valid New", Price = 20.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow },
            new Product { Name = "Invalid New", Price = -5.00m, Stock = 100, LastModified = DateTimeOffset.UtcNow }
        };

        context.ChangeTracker.Clear();

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert(
            existingProducts.Concat(newProducts),
            new UpsertOptions { Strategy = BatchStrategy.OneByOne });

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.FailureCount.ShouldBe(2);
        result.DatabaseRoundTrips.ShouldBe(5);
    }

    [Fact]
    public void Upsert_DivideAndConquer_FewerRoundTrips()
    {
        using var context = CreateContext();
        SeedData(context, 10);

        var existingProducts = context.Products.ToList();
        foreach (var p in existingProducts)
            p.Price += 1.00m;

        var newProducts = Enumerable.Range(1, 10).Select(i => new Product
        {
            Name = $"D&C Product {i}",
            Price = 30.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        context.ChangeTracker.Clear();

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert(
            existingProducts.Concat(newProducts),
            new UpsertOptions { Strategy = BatchStrategy.DivideAndConquer });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(20);
        result.DatabaseRoundTrips.ShouldBeLessThan(20);
    }

    [Fact]
    public void Upsert_DivideAndConquer_FallsBackOnFailure()
    {
        using var context = CreateContext();
        SeedData(context, 8);

        var existingProducts = context.Products.ToList();
        existingProducts[3].Price = -10.00m; // Invalid in middle

        var newProducts = Enumerable.Range(1, 4).Select(i => new Product
        {
            Name = $"Fallback Product {i}",
            Price = i == 2 ? -5.00m : 25.00m + i, // One invalid
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        context.ChangeTracker.Clear();

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert(
            existingProducts.Concat(newProducts),
            new UpsertOptions { Strategy = BatchStrategy.DivideAndConquer });

        result.IsPartialSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(10);
        result.FailureCount.ShouldBe(2);
    }

    [Fact]
    public void Upsert_Strategies_ProduceSameResults()
    {
        using var context1 = CreateContext();
        using var context2 = CreateContext();

        SeedData(context1, 10);
        SeedData(context2, 10);

        var oneByOneProducts = context1.Products.ToList();
        var divideAndConquerProducts = context2.Products.ToList();

        for (int i = 0; i < oneByOneProducts.Count; i++)
        {
            if (i % 4 == 0)
            {
                oneByOneProducts[i].Price = -10.00m;
                divideAndConquerProducts[i].Price = -10.00m;
            }
            else
            {
                oneByOneProducts[i].Price += 5.00m;
                divideAndConquerProducts[i].Price += 5.00m;
            }
        }

        context1.ChangeTracker.Clear();
        context2.ChangeTracker.Clear();

        var saver1 = new Winnower<Product, int>(context1);
        var saver2 = new Winnower<Product, int>(context2);

        var oneByOneResult = saver1.Upsert(
            oneByOneProducts,
            new UpsertOptions { Strategy = BatchStrategy.OneByOne });

        var divideAndConquerResult = saver2.Upsert(
            divideAndConquerProducts,
            new UpsertOptions { Strategy = BatchStrategy.DivideAndConquer });

        oneByOneResult.SuccessCount.ShouldBe(divideAndConquerResult.SuccessCount);
        oneByOneResult.FailureCount.ShouldBe(divideAndConquerResult.FailureCount);
    }

    [Fact]
    public void Upsert_FailureRate0_DivideAndConquerOptimal()
    {
        using var context = CreateContext();
        SeedData(context, 100);

        var existingProducts = context.Products.ToList();
        foreach (var p in existingProducts)
            p.Price += 1.00m;

        var newProducts = Enumerable.Range(1, 100).Select(i => new Product
        {
            Name = $"0% Failure Product {i}",
            Price = 20.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        context.ChangeTracker.Clear();

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert(
            existingProducts.Concat(newProducts),
            new UpsertOptions { Strategy = BatchStrategy.DivideAndConquer });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(200);
        result.DatabaseRoundTrips.ShouldBeLessThan(200);
    }

    [Fact]
    public void Upsert_FailureRate25_DivideAndConquerGood()
    {
        using var context = CreateContext();
        SeedData(context, 40);

        var products = context.Products.ToList();
        for (int i = 0; i < products.Count; i++)
        {
            if (i % 4 == 0)
                products[i].Price = -10.00m;
            else
                products[i].Price += 1.00m;
        }

        context.ChangeTracker.Clear();

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert(
            products,
            new UpsertOptions { Strategy = BatchStrategy.DivideAndConquer });

        result.SuccessCount.ShouldBe(30);
        result.FailureCount.ShouldBe(10);
    }

    [Fact]
    public void Upsert_FailureRate50_StrategiesCompetitive()
    {
        using var context = CreateContext();
        SeedData(context, 20);

        var products = context.Products.ToList();
        for (int i = 0; i < products.Count; i++)
        {
            if (i % 2 == 0)
                products[i].Price = -10.00m;
            else
                products[i].Price += 1.00m;
        }

        context.ChangeTracker.Clear();

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert(
            products,
            new UpsertOptions { Strategy = BatchStrategy.DivideAndConquer });

        result.SuccessCount.ShouldBe(10);
        result.FailureCount.ShouldBe(10);
    }

    [Fact]
    public void Upsert_FailureRate100_BothHandleCorrectly()
    {
        using var context = CreateContext();
        SeedData(context, 20);

        var products = context.Products.ToList();
        foreach (var p in products)
            p.Price = -10.00m;

        context.ChangeTracker.Clear();

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert(
            products,
            new UpsertOptions { Strategy = BatchStrategy.DivideAndConquer });

        result.IsCompleteFailure.ShouldBeTrue();
        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(20);
    }

    [Fact]
    public void UpsertGraph_OneByOne_Works()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 5, itemsPerOrder: 2);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        foreach (var order in orders)
            order.Status = CustomerOrderStatus.Processing;

        context.ChangeTracker.Clear();

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpsertGraph(orders, new UpsertGraphOptions
        {
            Strategy = BatchStrategy.OneByOne
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(5);
        result.DatabaseRoundTrips.ShouldBe(5);
    }

    [Fact]
    public void UpsertGraph_DivideAndConquer_Works()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 10, itemsPerOrder: 2);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        foreach (var order in orders)
            order.Status = CustomerOrderStatus.Completed;

        context.ChangeTracker.Clear();

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpsertGraph(orders, new UpsertGraphOptions
        {
            Strategy = BatchStrategy.DivideAndConquer
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(10);
        result.DatabaseRoundTrips.ShouldBeLessThan(10);
    }
}
