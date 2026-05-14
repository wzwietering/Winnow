using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

/// <summary>
/// Sync analogue of <see cref="WinnowerUpsertMatchByConcurrencyTests"/>. Verifies that
/// <see cref="Winnow.Internal.DuplicateKeyHandler{TEntity, TKey}.RetryAsUpdate"/> uses the
/// MatchBy refresh path to recover the conflicting row's PK before retrying as UPDATE.
/// </summary>
public class WinnowerUpsertMatchBySyncRetryTests : TestBase
{
    [Fact]
    public void Upsert_MatchBy_RaceCondition_WithRetryAsUpdate_FlipsToUpdate()
    {
        using var context = CreateContext();

        var incoming = new CustomerOrder
        {
            OrderNumber = "RACE-SYNC",
            CustomerId = 1,
            CustomerName = "Incoming",
            TotalAmount = 50m,
            OrderDate = DateTimeOffset.UtcNow
        };

        InjectConflictingRowOnce(context, "RACE-SYNC", "Concurrent");

        var options = new UpsertOptions { DuplicateKeyStrategy = DuplicateKeyStrategy.RetryAsUpdate }
            .WithMatchBy<CustomerOrder, string>(o => o.OrderNumber);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.Upsert(new[] { incoming }, options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(1);
        result.InsertedCount.ShouldBe(0);

        context.ChangeTracker.Clear();
        var reloaded = context.CustomerOrders.Single(o => o.OrderNumber == "RACE-SYNC");
        reloaded.CustomerName.ShouldBe("Incoming");
        incoming.Id.ShouldBe(reloaded.Id);
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
            rowsAffected.ShouldBe(1);
        };
    }
}
