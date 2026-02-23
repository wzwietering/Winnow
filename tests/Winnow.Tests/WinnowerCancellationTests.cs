using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

public class WinnowerCancellationTests : TestBase
{
    [Fact]
    public async Task InsertAsync_PreCancelledToken_ReturnsWasCancelled()
    {
        using var context = CreateContext();
        var products = CreateProducts(5);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var saver = new Winnower<Product, int>(context);
        var result = await saver.InsertAsync(products, cts.Token);

        result.WasCancelled.ShouldBeTrue();
        result.SuccessCount.ShouldBe(0);
    }

    [Fact]
    public async Task UpdateAsync_PreCancelledToken_ReturnsWasCancelled()
    {
        using var context = CreateContext();
        SeedData(context, 5);

        var products = context.Products.ToList();
        foreach (var p in products)
            p.Price += 1.00m;

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var saver = new Winnower<Product, int>(context);
        var result = await saver.UpdateAsync(products, cts.Token);

        result.WasCancelled.ShouldBeTrue();
        result.SuccessCount.ShouldBe(0);
    }

    [Fact]
    public async Task DeleteAsync_PreCancelledToken_ReturnsWasCancelled()
    {
        using var context = CreateContext();
        SeedData(context, 5);
        var products = context.Products.ToList();

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var saver = new Winnower<Product, int>(context);
        var result = await saver.DeleteAsync(products, cts.Token);

        result.WasCancelled.ShouldBeTrue();
        result.SuccessCount.ShouldBe(0);
    }

    [Fact]
    public async Task InsertGraphAsync_PreCancelledToken_ReturnsWasCancelled()
    {
        using var context = CreateContext();
        var orders = CreateOrders(3);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = await saver.InsertGraphAsync(orders, cts.Token);

        result.WasCancelled.ShouldBeTrue();
        result.SuccessCount.ShouldBe(0);
    }

    [Fact]
    public async Task UpdateGraphAsync_PreCancelledToken_ReturnsWasCancelled()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 2);

        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();
        foreach (var order in orders)
            order.Status = CustomerOrderStatus.Completed;

        context.ChangeTracker.Clear();

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = await saver.UpdateGraphAsync(orders, cts.Token);

        result.WasCancelled.ShouldBeTrue();
        result.SuccessCount.ShouldBe(0);
    }

    [Fact]
    public async Task DeleteGraphAsync_PreCancelledToken_ReturnsWasCancelled()
    {
        using var context = CreateContext();
        SeedCustomerOrders(context, 3, itemsPerOrder: 2);
        var orders = context.CustomerOrders.Include(o => o.OrderItems).ToList();

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = await saver.DeleteGraphAsync(orders, cts.Token);

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
