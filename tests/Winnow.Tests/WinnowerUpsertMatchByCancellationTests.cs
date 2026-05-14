using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

/// <summary>
/// Verifies the MatchBy duplicate-key retry path uses async I/O when called from the async pipeline,
/// rather than blocking a thread-pool thread on a synchronous SELECT (sync-over-async).
/// The fix that makes this pass also threads <see cref="CancellationToken"/> through to the SELECT.
/// </summary>
public class WinnowerUpsertMatchByCancellationTests : TestBase
{
    [Fact]
    public async Task UpsertAsync_MatchBy_RetryPath_DoesNotIssueSyncSelectInAsyncPipeline()
    {
        var probe = new ReaderModeProbe();
        using var context = CreateContextWithInterceptor(probe);

        var incoming = new CustomerOrder
        {
            OrderNumber = "RACE-1",
            CustomerId = 1,
            CustomerName = "Incoming",
            TotalAmount = 50m,
            OrderDate = DateTimeOffset.UtcNow
        };

        InjectConflictingRowOnce(context, "RACE-1", "Concurrent");

        var options = new UpsertOptions { DuplicateKeyStrategy = DuplicateKeyStrategy.RetryAsUpdate }
            .WithMatchBy<CustomerOrder, string>(o => o.OrderNumber);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = await saver.UpsertAsync(new[] { incoming }, options);

        result.UpdatedCount.ShouldBe(1, "retry-as-update path must succeed for this test to be meaningful");

        // BUG today: the duplicate-key retry path calls a synchronous TryRefreshFromMatchBy,
        // issuing the refresh SELECT via blocking I/O from inside the async pipeline.
        // FIXED: TryRefreshFromMatchByAsync is called and the SELECT goes through async I/O.
        probe.SyncSelectsAgainstCustomerOrders.ShouldBe(0,
            "MatchBy retry path issued a sync SELECT inside an async pipeline (sync-over-async).");
    }

    private static TestDbContext CreateContextWithInterceptor(IInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .AddInterceptors(interceptor)
            .Options;

        var context = new TestDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
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

    private sealed class ReaderModeProbe : DbCommandInterceptor
    {
        public int SyncSelectsAgainstCustomerOrders { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            if (IsSelectCustomerOrders(command.CommandText)) SyncSelectsAgainstCustomerOrders++;
            return base.ReaderExecuting(command, eventData, result);
        }

        private static bool IsSelectCustomerOrders(string sql)
        {
            var trimmed = sql.AsSpan().TrimStart();
            return trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("CustomerOrders", StringComparison.Ordinal);
        }
    }
}
