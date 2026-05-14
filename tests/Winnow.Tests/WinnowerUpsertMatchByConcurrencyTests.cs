using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

/// <summary>
/// Race-condition tests for MatchBy. SQLite has file-level locking so true concurrency is
/// modelled by hooking <see cref="DbContext.SavingChanges"/> to inject a conflicting row
/// after the pre-SELECT runs but before our INSERT commits.
/// </summary>
public class WinnowerUpsertMatchByConcurrencyTests : TestBase
{
    [Fact]
    public async Task UpsertAsync_MatchBy_RaceCondition_WithRetryAsUpdate_FlipsToUpdate()
    {
        using var context = CreateContext();

        var incoming = new CustomerOrder
        {
            OrderNumber = "RACE-1",
            CustomerId = 1,
            CustomerName = "Incoming",
            TotalAmount = 50m,
            OrderDate = DateTimeOffset.UtcNow
        };

        InjectConflictingRowOnce(context, orderNumber: "RACE-1", customerName: "Concurrent");

        var options = new UpsertOptions { DuplicateKeyStrategy = DuplicateKeyStrategy.RetryAsUpdate }
            .WithMatchBy<CustomerOrder, string>(o => o.OrderNumber);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = await saver.UpsertAsync(new[] { incoming }, options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(1);
        result.InsertedCount.ShouldBe(0);

        context.ChangeTracker.Clear();
        var reloaded = context.CustomerOrders.Single(o => o.OrderNumber == "RACE-1");
        reloaded.CustomerName.ShouldBe("Incoming");  // Our UPDATE overwrote the concurrent insert's value.
        incoming.Id.ShouldBe(reloaded.Id);            // Resolved PK was copied onto the input entity.
    }

    [Fact]
    public async Task UpsertAsync_MatchBy_RaceCondition_WithFailStrategy_RecordsFailure()
    {
        using var context = CreateContext();

        var incoming = new CustomerOrder
        {
            OrderNumber = "RACE-FAIL",
            CustomerId = 1,
            CustomerName = "Incoming",
            TotalAmount = 50m,
            OrderDate = DateTimeOffset.UtcNow
        };

        InjectConflictingRowOnce(context, orderNumber: "RACE-FAIL", customerName: "Concurrent");

        var options = new UpsertOptions { DuplicateKeyStrategy = DuplicateKeyStrategy.Fail }
            .WithMatchBy<CustomerOrder, string>(o => o.OrderNumber);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = await saver.UpsertAsync(new[] { incoming }, options);

        result.FailureCount.ShouldBe(1);
        result.InsertedCount.ShouldBe(0);
        result.UpdatedCount.ShouldBe(0);
        result.Failures[0].Reason.ShouldBe(FailureReason.DuplicateKey);
    }

    private static void InjectConflictingRowOnce(TestDbContext context, string orderNumber, string customerName)
    {
        var fired = false;
        context.SavingChanges += (_, _) =>
        {
            if (fired) return;
            fired = true;
            var rowsAffected = context.Database.ExecuteSqlInterpolated(
                $@"INSERT INTO CustomerOrders (OrderNumber, CustomerId, CustomerName, Status, TotalAmount, OrderDate, Version)
                   VALUES ({orderNumber}, 1, {customerName}, 0, 1.00, '2020-01-01 00:00:00', X'0000000000000001')");
            // Guard against the test silently no-opping if a future schema change rejects this row.
            rowsAffected.ShouldBe(1);
        };
    }
}
