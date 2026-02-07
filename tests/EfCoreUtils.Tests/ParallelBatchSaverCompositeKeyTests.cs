using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests;

public class ParallelBatchSaverCompositeKeyTests : ParallelTestBase
{
    [Fact]
    public async Task InsertBatchAsync_CompositeKeyEntities_AllInserted()
    {
        EnsureDatabaseCreated();
        SeedOrdersForOrderLines();

        var factory = CreateContextFactory();
        var saver = new ParallelBatchSaver<OrderLine, CompositeKey>(factory, maxDegreeOfParallelism: 2);

        var orderLines = CreateOrderLines(6);
        var result = await saver.InsertBatchAsync(orderLines);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(6);
    }

    [Fact]
    public async Task UpdateBatchAsync_CompositeKeyEntities_AllUpdated()
    {
        EnsureDatabaseCreated();
        SeedOrdersForOrderLines();
        SeedOrderLines(6);

        var factory = CreateContextFactory();
        var saver = new ParallelBatchSaver<OrderLine, CompositeKey>(factory, maxDegreeOfParallelism: 2);

        var orderLines = QueryWithFactory(ctx => ctx.OrderLines.ToList());
        foreach (var ol in orderLines) ol.Quantity += 1;

        var result = await saver.UpdateBatchAsync(orderLines);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(6);
    }

    [Fact]
    public async Task UpsertBatchAsync_CompositeKeyEntities_ExistingUpdated()
    {
        EnsureDatabaseCreated();
        SeedOrdersForOrderLines();
        SeedOrderLines(4);

        var factory = CreateContextFactory();
        var saver = new ParallelBatchSaver<OrderLine, CompositeKey>(factory, maxDegreeOfParallelism: 2);

        var existing = QueryWithFactory(ctx => ctx.OrderLines.ToList());
        foreach (var ol in existing) ol.Quantity += 1;

        var result = await saver.UpsertBatchAsync(existing);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(existing.Count);
    }

    [Fact]
    public void AutoDetect_OrderLine_ReturnsIsCompositeKey()
    {
        EnsureDatabaseCreated();
        var saver = new ParallelBatchSaver<OrderLine>(CreateContextFactory(), 2);

        saver.IsCompositeKey.ShouldBeTrue();
    }

    [Fact]
    public async Task ResultMerging_CompositeKey_PreservesKeyValues()
    {
        EnsureDatabaseCreated();
        SeedOrdersForOrderLines();

        var factory = CreateContextFactory();
        var saver = new ParallelBatchSaver<OrderLine, CompositeKey>(factory, maxDegreeOfParallelism: 2);

        var orderLines = CreateOrderLines(4);
        var result = await saver.InsertBatchAsync(orderLines);

        result.IsCompleteSuccess.ShouldBeTrue();
        foreach (var inserted in result.InsertedEntities)
        {
            inserted.Id.Count.ShouldBe(2);
        }
    }

    private void SeedOrdersForOrderLines()
    {
        SeedWithFactory(ctx =>
        {
            ctx.CustomerOrders.AddRange(
                new CustomerOrder
                {
                    Id = 1, OrderNumber = "ORD-CK-1", CustomerId = 1,
                    CustomerName = "Customer 1", TotalAmount = 100, OrderDate = DateTimeOffset.UtcNow
                },
                new CustomerOrder
                {
                    Id = 2, OrderNumber = "ORD-CK-2", CustomerId = 2,
                    CustomerName = "Customer 2", TotalAmount = 100, OrderDate = DateTimeOffset.UtcNow
                });
            ctx.SaveChanges();
        });
    }

    private void SeedOrderLines(int count)
    {
        SeedWithFactory(ctx =>
        {
            for (int i = 1; i <= count; i++)
            {
                ctx.OrderLines.Add(new OrderLine
                {
                    OrderId = (i % 2) + 1,
                    LineNumber = i,
                    Quantity = 1,
                    UnitPrice = 10m
                });
            }
            ctx.SaveChanges();
        });
    }

    private static List<OrderLine> CreateOrderLines(int count)
    {
        return Enumerable.Range(1, count).Select(i => new OrderLine
        {
            OrderId = (i % 2) + 1,
            LineNumber = i + 200,
            Quantity = 1,
            UnitPrice = 10m
        }).ToList();
    }
}
