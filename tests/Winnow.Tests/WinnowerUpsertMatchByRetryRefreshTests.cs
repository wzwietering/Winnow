using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

/// <summary>
/// Models the race where another client inserts the matching row, our INSERT collides, and
/// the same client then deletes the row before our retry refresh runs. The retry path must
/// surface this as a distinct, well-classified failure rather than blindly attempting an
/// UPDATE against the original default PK (which fails as a misleading concurrency error).
/// </summary>
public class WinnowerUpsertMatchByRetryRefreshTests : TestBase
{
    [Fact]
    public async Task UpsertAsync_MatchBy_RetryPath_WhenRefreshFindsNoRow_RecordsMatchByRefreshNotFound()
    {
        using var context = CreateContext();
        InjectConflictingRowThenDeleteOnFailure(context, "RACE-REFRESH-ASYNC");

        var incoming = NewOrder("RACE-REFRESH-ASYNC", "Incoming");
        var options = new UpsertOptions { DuplicateKeyStrategy = DuplicateKeyStrategy.RetryAsUpdate }
            .WithMatchBy<CustomerOrder, string>(o => o.OrderNumber);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = await saver.UpsertAsync(new[] { incoming }, options);

        result.FailureCount.ShouldBe(1);
        var failure = result.Failures.Single();
        failure.Reason.ShouldBe(FailureReason.MatchByRefreshNotFound);
        failure.ErrorMessage.ShouldContain("match", Case.Insensitive);
    }

    [Fact]
    public void Upsert_MatchBy_RetryPath_WhenRefreshFindsNoRow_RecordsMatchByRefreshNotFound()
    {
        using var context = CreateContext();
        InjectConflictingRowThenDeleteOnFailure(context, "RACE-REFRESH-SYNC");

        var incoming = NewOrder("RACE-REFRESH-SYNC", "Incoming");
        var options = new UpsertOptions { DuplicateKeyStrategy = DuplicateKeyStrategy.RetryAsUpdate }
            .WithMatchBy<CustomerOrder, string>(o => o.OrderNumber);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.Upsert(new[] { incoming }, options);

        result.FailureCount.ShouldBe(1);
        var failure = result.Failures.Single();
        failure.Reason.ShouldBe(FailureReason.MatchByRefreshNotFound);
        failure.ErrorMessage.ShouldContain("match", Case.Insensitive);
    }

    private static CustomerOrder NewOrder(string orderNumber, string customerName) => new()
    {
        OrderNumber = orderNumber,
        CustomerId = 1,
        CustomerName = customerName,
        TotalAmount = 50m,
        OrderDate = DateTimeOffset.UtcNow
    };

    private static void InjectConflictingRowThenDeleteOnFailure(TestDbContext context, string orderNumber)
    {
        var inserted = false;
        var deleted = false;

        context.SavingChanges += (_, _) =>
        {
            if (inserted) return;
            inserted = true;
            var rowsAffected = context.Database.ExecuteSqlInterpolated(
                $@"INSERT INTO CustomerOrders (OrderNumber, CustomerId, CustomerName, Status, TotalAmount, OrderDate, Version)
                   VALUES ({orderNumber}, 1, 'Concurrent', 0, 1.00, '2020-01-01 00:00:00', X'0000000000000001')");
            rowsAffected.ShouldBe(1);
        };

        context.SaveChangesFailed += (_, _) =>
        {
            if (deleted) return;
            deleted = true;
            context.Database.ExecuteSqlInterpolated(
                $"DELETE FROM CustomerOrders WHERE OrderNumber = {orderNumber}");
        };
    }
}
