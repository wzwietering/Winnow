using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

public class BatchSaverUpsertAsyncTests : TestBase
{
    [Fact]
    public async Task UpsertBatchAsync_Works()
    {
        using var context = CreateContext();
        SeedData(context, 3);

        var existingProducts = context.Products.ToList();
        foreach (var p in existingProducts)
            p.Price += 5.00m;

        var newProducts = Enumerable.Range(1, 2).Select(i => new Product
        {
            Name = $"Async Product {i}",
            Price = 25.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = await saver.UpsertBatchAsync(existingProducts.Concat(newProducts));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(2);
        result.UpdatedCount.ShouldBe(3);

        context.ChangeTracker.Clear();
        context.Products.Count().ShouldBe(5);
    }

    [Fact]
    public async Task UpsertBatchAsync_WithOptions_Works()
    {
        using var context = CreateContext();
        SeedData(context, 5);

        var products = context.Products.ToList();
        foreach (var p in products)
            p.Price += 2.00m;

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Product, int>(context);
        var result = await saver.UpsertBatchAsync(
            products,
            new UpsertBatchOptions { Strategy = BatchStrategy.DivideAndConquer });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(5);
        result.DatabaseRoundTrips.ShouldBeLessThan(5);
    }

    [Fact]
    public async Task UpsertBatchAsync_Cancellation_ReturnsWasCancelled()
    {
        using var context = CreateContext();

        var products = Enumerable.Range(1, 5).Select(i => new Product
        {
            Name = $"Cancel Product {i}",
            Price = 10.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var saver = new BatchSaver<Product, int>(context);

        var result = await saver.UpsertBatchAsync(products, cts.Token);

        result.WasCancelled.ShouldBeTrue();
        result.IsCompleteSuccess.ShouldBeFalse();
        result.SuccessCount.ShouldBe(0);
    }

    [Fact]
    public async Task UpsertGraphBatchAsync_Works()
    {
        using var context = CreateContext();

        var orders = Enumerable.Range(1, 3).Select(i => new CustomerOrder
        {
            OrderNumber = $"ORD-ASYNC-{i:D3}",
            CustomerName = $"Async Customer {i}",
            CustomerId = 1000 + i,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 100.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems = Enumerable.Range(1, 2).Select(j => new OrderItem
            {
                ProductId = 1000 + j,
                ProductName = $"Async Product {j}",
                Quantity = j + 1,
                UnitPrice = 25.00m,
                Subtotal = (j + 1) * 25.00m
            }).ToList()
        }).ToList();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = await saver.UpsertGraphBatchAsync(orders);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(3);

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(3);
        context.OrderItems.Count().ShouldBe(6);
    }

    [Fact]
    public async Task UpsertGraphBatchAsync_WithOptions_Works()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 5, itemsPerOrder: 2);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        foreach (var order in orders)
            order.Status = CustomerOrderStatus.Completed;

        context.ChangeTracker.Clear();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = await saver.UpsertGraphBatchAsync(orders, new UpsertGraphBatchOptions
        {
            Strategy = BatchStrategy.DivideAndConquer
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(5);
    }

    [Fact]
    public async Task UpsertGraphBatchAsync_Cancellation_ReturnsWasCancelled()
    {
        using var context = CreateContext();

        var orders = Enumerable.Range(1, 3).Select(i => new CustomerOrder
        {
            OrderNumber = $"ORD-CANCEL-{i:D3}",
            CustomerName = $"Cancel Customer {i}",
            CustomerId = 2000 + i,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 50.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = 2000 + i,
                    ProductName = $"Cancel Product {i}",
                    Quantity = 1,
                    UnitPrice = 50.00m,
                    Subtotal = 50.00m
                }
            ]
        }).ToList();

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var saver = new BatchSaver<CustomerOrder, int>(context);

        var result = await saver.UpsertGraphBatchAsync(orders, cts.Token);

        result.WasCancelled.ShouldBeTrue();
        result.IsCompleteSuccess.ShouldBeFalse();
        result.SuccessCount.ShouldBe(0);
    }
}
