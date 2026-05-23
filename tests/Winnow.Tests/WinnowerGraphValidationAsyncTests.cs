using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

/// <summary>
/// Async × graph × pre-validation coverage. The sync graph paths are covered
/// by <see cref="WinnowerGraphValidationTests"/>; the flat async paths by
/// <see cref="WinnowerValidationAsyncTests"/>. This file exercises the
/// intersection (graph + async) through
/// <c>OperationPreValidationHelper</c>'s async dispatch — including
/// cancellation polling mid-validation, which routes differently in the graph
/// strategies than in the flat ones.
/// </summary>
public class WinnowerGraphValidationAsyncTests : TestBase
{
    private static ValidatorDelegate<CustomerOrder> RejectOrderNumber(string orderNumber)
        => (CustomerOrder o, ref ValidationCollector c) =>
        {
            if (o.OrderNumber == orderNumber)
                c.Add(nameof(CustomerOrder.OrderNumber), "Rejected");
        };

    private static CustomerOrder NewOrder(string orderNumber, int itemCount = 1) => new()
    {
        OrderNumber = orderNumber,
        CustomerName = "Test",
        CustomerId = 1,
        TotalAmount = 1m,
        OrderDate = DateTimeOffset.UtcNow,
        OrderItems = Enumerable.Range(1, itemCount).Select(i => new OrderItem
        {
            ProductId = 100 + i,
            ProductName = $"P{i}",
            Quantity = 1,
            UnitPrice = 1m,
            Subtotal = 1m,
        }).ToList(),
    };

    private static List<CustomerOrder> LargeOrderBatch(int n) =>
        Enumerable.Range(0, n).Select(i => NewOrder($"O{i}")).ToList();

    [Fact]
    public async Task InsertGraphAsync_PreValidation_RejectsBadOrderAndPersistsRest()
    {
        using var context = CreateContext();
        var orders = new[] { NewOrder("OK"), NewOrder("BAD") };

        var options = new InsertGraphOptions()
            .WithValidation(RejectOrderNumber("BAD"));

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = await saver.InsertGraphAsync(orders, options);

        result.SuccessCount.ShouldBe(1);
        result.FailureCount.ShouldBe(1);
        result.Failures.ShouldHaveSingleItem().EntityIndex.ShouldBe(1);

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count(o => o.OrderNumber == "BAD").ShouldBe(0);
        context.CustomerOrders.Count(o => o.OrderNumber == "OK").ShouldBe(1);
    }

    [Fact]
    public async Task InsertGraphAsync_CancelledMidValidation_ThrowsOperationCanceled()
    {
        using var context = CreateContext();
        var orders = LargeOrderBatch(500);

        using var cts = new CancellationTokenSource();
        var holder = new Counter();
        var options = new InsertGraphOptions();
        options.WithValidation<CustomerOrder>((CustomerOrder _, ref ValidationCollector _) =>
        {
            if (Interlocked.Increment(ref holder.Value) == 50) cts.Cancel();
        });
        options.Validation!.CancellationCheckInterval = 1;

        var saver = new Winnower<CustomerOrder, int>(context);
        await Should.ThrowAsync<OperationCanceledException>(
            () => saver.InsertGraphAsync(orders, options, cts.Token));
    }

    [Fact]
    public async Task UpdateGraphAsync_PreValidation_RejectsBadOrderAndKeepsOriginal()
    {
        using var context = CreateContext();
        var order = NewOrder("ORIG");
        context.CustomerOrders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        order.OrderNumber = "BAD";
        var options = new GraphOptions().WithValidation(RejectOrderNumber("BAD"));

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = await saver.UpdateGraphAsync([order], options);

        result.FailureCount.ShouldBe(1);
        result.Failures.ShouldHaveSingleItem().Reason.ShouldBe(FailureReason.ValidationError);

        context.ChangeTracker.Clear();
        (await context.CustomerOrders.AsNoTracking().SingleAsync()).OrderNumber.ShouldBe("ORIG");
    }

    [Fact]
    public async Task UpdateGraphAsync_CancelledMidValidation_ThrowsOperationCanceled()
    {
        using var context = CreateContext();
        // Pre-seed so UpdateGraphAsync has something to track.
        var seeded = NewOrder("seed-0");
        context.CustomerOrders.Add(seeded);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var orders = new List<CustomerOrder> { seeded };
        for (int i = 1; i < 500; i++)
            orders.Add(new CustomerOrder
            {
                Id = i + 10_000,
                OrderNumber = $"u{i}",
                CustomerName = "C",
                CustomerId = 1,
                TotalAmount = 1m,
                OrderDate = DateTimeOffset.UtcNow,
                OrderItems = [],
            });

        using var cts = new CancellationTokenSource();
        var holder = new Counter();
        var options = new GraphOptions();
        options.WithValidation<CustomerOrder>((CustomerOrder _, ref ValidationCollector _) =>
        {
            if (Interlocked.Increment(ref holder.Value) == 50) cts.Cancel();
        });
        options.Validation!.CancellationCheckInterval = 1;

        var saver = new Winnower<CustomerOrder, int>(context);
        await Should.ThrowAsync<OperationCanceledException>(
            () => saver.UpdateGraphAsync(orders, options, cts.Token));
    }

    [Fact]
    public async Task DeleteGraphAsync_PreValidation_RejectsBadOrderAndKeepsRow()
    {
        using var context = CreateContext();
        var order = NewOrder("KEEP-ME");
        context.CustomerOrders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var attached = await context.CustomerOrders.AsNoTracking().Include(o => o.OrderItems).SingleAsync();

        var options = new DeleteGraphOptions().WithValidation(RejectOrderNumber("KEEP-ME"));

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = await saver.DeleteGraphAsync([attached], options);

        result.FailureCount.ShouldBe(1);
        result.Failures.ShouldHaveSingleItem().Reason.ShouldBe(FailureReason.ValidationError);

        context.ChangeTracker.Clear();
        (await context.CustomerOrders.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task UpsertGraphAsync_PreValidation_RejectsBadAndPersistsGood()
    {
        using var context = CreateContext();
        var orders = new[] { NewOrder("OK"), NewOrder("BAD") };

        var options = new UpsertGraphOptions().WithValidation(RejectOrderNumber("BAD"));

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = await saver.UpsertGraphAsync(orders, options);

        result.SuccessCount.ShouldBe(1);
        result.FailureCount.ShouldBe(1);
        result.Failures.ShouldHaveSingleItem().Reason.ShouldBe(FailureReason.ValidationError);

        context.ChangeTracker.Clear();
        (await context.CustomerOrders.CountAsync(o => o.OrderNumber == "BAD")).ShouldBe(0);
        (await context.CustomerOrders.CountAsync(o => o.OrderNumber == "OK")).ShouldBe(1);
    }

    [Fact]
    public async Task UpsertGraphAsync_CancelledMidValidation_ThrowsOperationCanceled()
    {
        using var context = CreateContext();
        var orders = LargeOrderBatch(500);

        using var cts = new CancellationTokenSource();
        var holder = new Counter();
        var options = new UpsertGraphOptions();
        options.WithValidation<CustomerOrder>((CustomerOrder _, ref ValidationCollector _) =>
        {
            if (Interlocked.Increment(ref holder.Value) == 50) cts.Cancel();
        });
        options.Validation!.CancellationCheckInterval = 1;

        var saver = new Winnower<CustomerOrder, int>(context);
        await Should.ThrowAsync<OperationCanceledException>(
            () => saver.UpsertGraphAsync(orders, options, cts.Token));
    }

    [Fact]
    public async Task DeleteGraphAsync_CancelledMidValidation_ThrowsOperationCanceled()
    {
        using var context = CreateContext();
        var seeded = NewOrder("seed-0");
        context.CustomerOrders.Add(seeded);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var attached = await context.CustomerOrders.AsNoTracking().Include(o => o.OrderItems).SingleAsync();
        var orders = new List<CustomerOrder> { attached };
        for (int i = 1; i < 500; i++)
            orders.Add(new CustomerOrder
            {
                Id = i + 20_000,
                OrderNumber = $"d{i}",
                CustomerName = "C",
                CustomerId = 1,
                TotalAmount = 1m,
                OrderDate = DateTimeOffset.UtcNow,
                OrderItems = [],
            });

        using var cts = new CancellationTokenSource();
        var holder = new Counter();
        var options = new DeleteGraphOptions();
        options.WithValidation<CustomerOrder>((CustomerOrder _, ref ValidationCollector _) =>
        {
            if (Interlocked.Increment(ref holder.Value) == 50) cts.Cancel();
        });
        options.Validation!.CancellationCheckInterval = 1;

        var saver = new Winnower<CustomerOrder, int>(context);
        await Should.ThrowAsync<OperationCanceledException>(
            () => saver.DeleteGraphAsync(orders, options, cts.Token));
    }

    [Fact]
    public async Task UpdateGraphAsync_ThrowBehavior_ThrowsWinnowValidationException()
    {
        using var context = CreateContext();
        var order = NewOrder("ORIG");
        context.CustomerOrders.Add(order);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        order.OrderNumber = "BAD";
        var options = new GraphOptions()
            .WithValidation(RejectOrderNumber("BAD"), ValidationFailureBehavior.Throw);

        var saver = new Winnower<CustomerOrder, int>(context);
        var ex = await Should.ThrowAsync<WinnowValidationException>(
            () => saver.UpdateGraphAsync([order], options));
        ex.Failures.ShouldHaveSingleItem().EntityIndex.ShouldBe(0);

        context.ChangeTracker.Clear();
        (await context.CustomerOrders.AsNoTracking().SingleAsync()).OrderNumber.ShouldBe("ORIG");
    }

    [Fact]
    public async Task UpsertGraphAsync_ThrowBehavior_ThrowsWinnowValidationException()
    {
        using var context = CreateContext();
        var orders = new[] { NewOrder("OK"), NewOrder("BAD") };

        var options = new UpsertGraphOptions()
            .WithValidation(RejectOrderNumber("BAD"), ValidationFailureBehavior.Throw);

        var saver = new Winnower<CustomerOrder, int>(context);
        var ex = await Should.ThrowAsync<WinnowValidationException>(
            () => saver.UpsertGraphAsync(orders, options));
        ex.Failures.ShouldHaveSingleItem().EntityIndex.ShouldBe(1);

        context.ChangeTracker.Clear();
        (await context.CustomerOrders.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task InsertGraphAsync_ThrowBehavior_ThrowsWinnowValidationException()
    {
        using var context = CreateContext();
        var orders = new[] { NewOrder("OK"), NewOrder("BAD") };

        var options = new InsertGraphOptions()
            .WithValidation(RejectOrderNumber("BAD"), ValidationFailureBehavior.Throw);

        var saver = new Winnower<CustomerOrder, int>(context);
        var ex = await Should.ThrowAsync<WinnowValidationException>(
            () => saver.InsertGraphAsync(orders, options));
        ex.Failures.ShouldHaveSingleItem().EntityIndex.ShouldBe(1);

        context.ChangeTracker.Clear();
        (await context.CustomerOrders.CountAsync()).ShouldBe(0);
    }

    private sealed class Counter { public int Value; }
}
