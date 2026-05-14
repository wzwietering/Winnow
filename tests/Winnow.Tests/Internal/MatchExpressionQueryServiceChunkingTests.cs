using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using Winnow.Internal.Services;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests.Internal;

/// <summary>
/// Exercises <see cref="Winnow.Internal.Services.MatchExpressionQueryService"/> chunking
/// boundaries (>500 entities, wide composite keys) that are otherwise uncovered.
/// </summary>
public class MatchExpressionQueryServiceChunkingTests : TestBase
{
    [Fact]
    public void Upsert_MatchBy_BatchLargerThanSingleChunk_MatchesAllRows()
    {
        const int count = 501;
        using var context = CreateContext();
        SeedOrders(context, count);

        var batch = BuildBatchPreservingMatchKey(count, totalAmount: 999m);

        var options = new UpsertOptions()
            .WithMatchBy<CustomerOrder>(o => o.OrderNumber);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.Upsert(batch, options);

        result.UpdatedCount.ShouldBe(count, "all entities should match seeded rows across chunk boundary");
        result.InsertedCount.ShouldBe(0);
    }

    [Fact]
    public void Upsert_MatchBy_WideCompositeKey_BudgetReducedChunkSize_MatchesAllRows()
    {
        const int count = 600;
        using var context = CreateContext();
        SeedOrders(context, count);

        // Match on 4 columns; batch carries the SAME values for those columns and changes only TotalAmount.
        var batch = BuildBatchPreservingMatchKey(count, totalAmount: 999m);

        var options = new UpsertOptions()
            .WithMatchBy<CustomerOrder>(o => new { o.OrderNumber, o.CustomerId, o.CustomerName, o.Status });

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.Upsert(batch, options);

        // 4-column anonymous projection forces chunkSize = 1800 / 4 = 450 (< MaxChunkSize 500),
        // so a 600-entity batch produces 2 chunks.
        result.UpdatedCount.ShouldBe(count);
        result.InsertedCount.ShouldBe(0);
    }

    [Fact]
    public void QueryExisting_WithEmptyMatchProperties_ThrowsInvariantError_NotNullReference()
    {
        using var context = CreateContext();
        var service = new MatchExpressionQueryService(context);

        // Empty matchProperties + non-empty (but empty) tuple triggers the predicate-builder
        // null-combined branch. Should be a clear InvalidOperationException, not a NullRef.
        IReadOnlyList<IProperty> emptyProperties = Array.Empty<IProperty>();
        var tuples = new[] { Array.Empty<object?>() };

        Should.Throw<InvalidOperationException>(() =>
            service.QueryExisting<CustomerOrder>(emptyProperties, tuples));
    }

    private static void SeedOrders(TestDbContext context, int count)
    {
        for (var i = 0; i < count; i++)
        {
            context.CustomerOrders.Add(new CustomerOrder
            {
                OrderNumber = $"ORD-{i:D5}",
                CustomerId = 1,
                CustomerName = "Original",
                Status = CustomerOrderStatus.Pending,
                TotalAmount = 1m,
                OrderDate = DateTimeOffset.UtcNow
            });
        }
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }

    private static CustomerOrder[] BuildBatchPreservingMatchKey(int count, decimal totalAmount)
    {
        var batch = new CustomerOrder[count];
        for (var i = 0; i < count; i++)
        {
            batch[i] = new CustomerOrder
            {
                OrderNumber = $"ORD-{i:D5}",
                CustomerId = 1,
                CustomerName = "Original",
                Status = CustomerOrderStatus.Pending,
                TotalAmount = totalAmount,
                OrderDate = DateTimeOffset.UtcNow
            };
        }
        return batch;
    }
}
