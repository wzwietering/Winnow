using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

public class BatchSaverCancellationTests : TestBase
{
    [Fact]
    public async Task InsertBatchAsync_PreCancelledToken_ReturnsWasCancelled()
    {
        using var context = CreateContext();
        var products = CreateProducts(5);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var saver = new BatchSaver<Product, int>(context);
        var result = await saver.InsertBatchAsync(products, cts.Token);

        result.WasCancelled.ShouldBeTrue();
        result.SuccessCount.ShouldBe(0);
    }

    [Fact]
    public async Task UpdateBatchAsync_PreCancelledToken_ReturnsWasCancelled()
    {
        using var context = CreateContext();
        SeedData(context, 5);

        var products = context.Products.ToList();
        foreach (var p in products)
            p.Price += 1.00m;

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var saver = new BatchSaver<Product, int>(context);
        var result = await saver.UpdateBatchAsync(products, cts.Token);

        result.WasCancelled.ShouldBeTrue();
        result.SuccessCount.ShouldBe(0);
    }

    [Fact]
    public async Task DeleteBatchAsync_PreCancelledToken_ReturnsWasCancelled()
    {
        using var context = CreateContext();
        SeedData(context, 5);
        var products = context.Products.ToList();

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var saver = new BatchSaver<Product, int>(context);
        var result = await saver.DeleteBatchAsync(products, cts.Token);

        result.WasCancelled.ShouldBeTrue();
        result.SuccessCount.ShouldBe(0);
    }

    [Fact]
    public async Task InsertGraphBatchAsync_PreCancelledToken_ReturnsWasCancelled()
    {
        using var context = CreateContext();
        var orders = CreateOrders(3);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = await saver.InsertGraphBatchAsync(orders, cts.Token);

        result.WasCancelled.ShouldBeTrue();
        result.SuccessCount.ShouldBe(0);
    }

    [Fact]
    public async Task UpdateGraphBatchAsync_PreCancelledToken_ReturnsWasCancelled()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 2);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        foreach (var order in orders)
            order.Status = CustomerOrderStatus.Completed;

        context.ChangeTracker.Clear();

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = await saver.UpdateGraphBatchAsync(orders, cts.Token);

        result.WasCancelled.ShouldBeTrue();
        result.SuccessCount.ShouldBe(0);
    }

    [Fact]
    public async Task DeleteGraphBatchAsync_PreCancelledToken_ReturnsWasCancelled()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 2);
        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var saver = new BatchSaver<CustomerOrder, int>(context);
        var result = await saver.DeleteGraphBatchAsync(orders, cts.Token);

        result.WasCancelled.ShouldBeTrue();
        result.SuccessCount.ShouldBe(0);
    }

    private static List<Product> CreateProducts(int count) =>
        Enumerable.Range(1, count).Select(i => new Product
        {
            Name = $"Cancel Product {i}",
            Price = 10.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

    private static List<CustomerOrder> CreateOrders(int count) =>
        Enumerable.Range(1, count).Select(i => new CustomerOrder
        {
            OrderNumber = $"ORD-CANCEL-{i:D3}",
            CustomerName = $"Cancel Customer {i}",
            CustomerId = 3000 + i,
            Status = CustomerOrderStatus.Pending,
            TotalAmount = 50.00m,
            OrderDate = DateTimeOffset.UtcNow,
            OrderItems =
            [
                new OrderItem
                {
                    ProductId = 3000 + i,
                    ProductName = $"Cancel Product {i}",
                    Quantity = 1,
                    UnitPrice = 50.00m,
                    Subtotal = 50.00m
                }
            ]
        }).ToList();
}
