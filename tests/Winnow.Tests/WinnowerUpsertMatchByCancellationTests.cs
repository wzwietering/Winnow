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

        MatchByTestHelpers.InjectConflictingRowOnce(context, "RACE-1", "Concurrent");

        var options = new UpsertOptions { DuplicateKeyStrategy = DuplicateKeyStrategy.RetryAsUpdate }
            .WithMatchBy<CustomerOrder, string>(o => o.OrderNumber);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = await saver.UpsertAsync(new[] { incoming }, options);

        result.UpdatedCount.ShouldBe(1, "retry-as-update path must succeed for this test to be meaningful");

        // Pins the async retry path: the SELECT must go through async I/O, not block on
        // a sync TryRefreshFromMatchBy call inside the async pipeline.
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
