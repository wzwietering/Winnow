using EfCoreUtils;
using EfCoreUtils.Tests.Entities;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests.CompositeKeyIntegration;

public class CompositeKeyAsyncTests : CompositeKeyTestBase
{
    [Fact]
    public async Task InsertBatchAsync_CompositeKey_Success()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLines = Enumerable.Range(1, 3).Select(i => new OrderLine
        {
            OrderId = orderId,
            LineNumber = i,
            ProductId = null,
            Quantity = i * 2,
            UnitPrice = 10.00m + i
        }).ToList();

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = await saver.InsertBatchAsync(orderLines);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
    }

    [Fact]
    public async Task UpdateBatchAsync_CompositeKey_Success()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);
        InsertOrderLines(context, orderId, 3);

        var orderLinesToUpdate = context.OrderLines.Where(ol => ol.OrderId == orderId).ToList();
        foreach (var line in orderLinesToUpdate)
        {
            line.Quantity += 1;
        }

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = await saver.UpdateBatchAsync(orderLinesToUpdate);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
    }

    [Fact]
    public async Task DeleteBatchAsync_CompositeKey_Success()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);
        InsertOrderLines(context, orderId, 3);

        var orderLinesToDelete = context.OrderLines.Where(ol => ol.OrderId == orderId).ToList();

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = await saver.DeleteBatchAsync(orderLinesToDelete);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);

        context.ChangeTracker.Clear();
        context.OrderLines.Count(ol => ol.OrderId == orderId).ShouldBe(0);
    }

    [Fact]
    public async Task InsertGraphBatchAsync_CompositeKey_Success()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLine = new OrderLine
        {
            OrderId = orderId,
            LineNumber = 1,
            ProductId = null,
            Quantity = 5,
            UnitPrice = 10.00m,
            Notes =
            [
                new OrderLineNote { Note = "Async Note 1", CreatedAt = DateTime.UtcNow },
                new OrderLineNote { Note = "Async Note 2", CreatedAt = DateTime.UtcNow }
            ]
        };

        var saver = new BatchSaver<OrderLine, CompositeKey>(context);
        var result = await saver.InsertGraphBatchAsync([orderLine]);

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var loaded = context.OrderLines
            .Include(ol => ol.Notes)
            .First(ol => ol.OrderId == orderId && ol.LineNumber == 1);
        loaded.Notes.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AutoDetect_AsyncOperations_Work()
    {
        using var context = CreateContext();
        var orderId = CreateCustomerOrder(context);

        var orderLine = new OrderLine
        {
            OrderId = orderId,
            LineNumber = 1,
            ProductId = null,
            Quantity = 5,
            UnitPrice = 10.00m
        };

        var saver = new BatchSaver<OrderLine>(context);
        var result = await saver.InsertBatchAsync([orderLine]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedIds[0].GetValue<int>(0).ShouldBe(orderId);
        result.InsertedIds[0].GetValue<int>(1).ShouldBe(1);
    }
}
