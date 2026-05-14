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
            .WithMatchBy<CustomerOrder>(o => o.OrderNumber);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = await saver.UpsertAsync(new[] { incoming }, options);

        result.UpdatedCount.ShouldBe(1, "retry-as-update path must succeed for this test to be meaningful");

        // Pins the async retry path: the SELECT must go through async I/O, not block on
        // a sync TryRefreshFromMatchBy call inside the async pipeline.
        probe.SyncSelectsAgainstCustomerOrders.ShouldBe(0,
            "MatchBy retry path issued a sync SELECT inside an async pipeline (sync-over-async).");
    }

    [Fact]
    public async Task UpsertAsync_MatchBy_CancellationDuringPreSelect_ThrowsOperationCancelled()
    {
        // Existing cancellation tests only cover pre-cancelled tokens. This test cancels
        // mid-flight (inside ReaderExecutingAsync), verifying that the pre-SELECT honours
        // the cancellation token threaded through QueryExistingAsync rather than completing
        // and silently swallowing the cancel.
        using var cts = new CancellationTokenSource();
        var interceptor = new CancelDuringSelectInterceptor(cts, "CANCEL-PRE-SELECT");
        using var context = CreateContextWithInterceptor(interceptor);

        var batch = new[]
        {
            new CustomerOrder
            {
                OrderNumber = "CANCEL-PRE-SELECT",
                CustomerId = 1,
                CustomerName = "Pre-select cancel",
                TotalAmount = 1m,
                OrderDate = DateTimeOffset.UtcNow
            }
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var options = new UpsertOptions().WithMatchBy<CustomerOrder>(o => o.OrderNumber);

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await saver.UpsertAsync(batch, options, cts.Token));

        interceptor.SawSelect.ShouldBeTrue("Test setup: the pre-SELECT must have been intercepted.");
    }

    [Fact]
    public async Task UpsertAsync_MatchBy_CancellationDuringRetryRefreshSelect_ThrowsOperationCancelled()
    {
        // The retry-refresh SELECT is a separate query from the pre-SELECT (fires only
        // after the initial INSERT collides with a concurrent row). The cancellation
        // token must propagate to that second SELECT too — without it, the retry path
        // would block on a sync-over-async I/O or ignore the cancel altogether.
        using var cts = new CancellationTokenSource();
        var interceptor = new CancelOnNthSelectInterceptor(cts, "RACE-RETRY-CANCEL", cancelOn: 2);
        using var context = CreateContextWithInterceptor(interceptor);

        MatchByTestHelpers.InjectConflictingRowOnce(context, "RACE-RETRY-CANCEL", "Concurrent");

        var batch = new[]
        {
            new CustomerOrder
            {
                OrderNumber = "RACE-RETRY-CANCEL",
                CustomerId = 1,
                CustomerName = "Incoming",
                TotalAmount = 1m,
                OrderDate = DateTimeOffset.UtcNow
            }
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var options = new UpsertOptions { DuplicateKeyStrategy = DuplicateKeyStrategy.RetryAsUpdate }
            .WithMatchBy<CustomerOrder>(o => o.OrderNumber);

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await saver.UpsertAsync(batch, options, cts.Token));

        interceptor.SelectsSeen.ShouldBeGreaterThanOrEqualTo(2,
            "Test setup: both the pre-SELECT and the retry-refresh SELECT must fire.");
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

    private sealed class CancelDuringSelectInterceptor : DbCommandInterceptor
    {
        private readonly CancellationTokenSource _cts;
        private readonly string _matchValueMarker;

        public CancelDuringSelectInterceptor(CancellationTokenSource cts, string matchValueMarker)
        {
            _cts = cts;
            _matchValueMarker = matchValueMarker;
        }

        public bool SawSelect { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsSelectMatchingMarker(command))
            {
                SawSelect = true;
                _cts.Cancel();
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private bool IsSelectMatchingMarker(DbCommand command)
        {
            var trimmed = command.CommandText.AsSpan().TrimStart();
            if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) return false;
            if (!command.CommandText.Contains("CustomerOrders", StringComparison.Ordinal)) return false;
            foreach (DbParameter p in command.Parameters)
            {
                if (p.Value is string s && s == _matchValueMarker) return true;
            }
            return false;
        }
    }

    private sealed class CancelOnNthSelectInterceptor : DbCommandInterceptor
    {
        private readonly CancellationTokenSource _cts;
        private readonly string _matchValueMarker;
        private readonly int _cancelOn;

        public CancelOnNthSelectInterceptor(CancellationTokenSource cts, string matchValueMarker, int cancelOn)
        {
            _cts = cts;
            _matchValueMarker = matchValueMarker;
            _cancelOn = cancelOn;
        }

        public int SelectsSeen { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (IsSelectMatchingMarker(command))
            {
                SelectsSeen++;
                if (SelectsSeen == _cancelOn) _cts.Cancel();
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private bool IsSelectMatchingMarker(DbCommand command)
        {
            var trimmed = command.CommandText.AsSpan().TrimStart();
            if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) return false;
            if (!command.CommandText.Contains("CustomerOrders", StringComparison.Ordinal)) return false;
            foreach (DbParameter p in command.Parameters)
            {
                if (p.Value is string s && s == _matchValueMarker) return true;
            }
            return false;
        }
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
