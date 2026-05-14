using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using Winnow.Internal;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

/// <summary>
/// Fills coverage gaps surfaced by the deep review: all-new / all-existing / empty
/// batches, the duplicate-key skip on null match values, graph upsert ignoring MatchBy,
/// and the MatchExpressionParser branch that rejects wrong-parameter-type lambdas.
/// </summary>
public class WinnowerUpsertMatchByCoverageGapTests : TestBase
{
    [Fact]
    public void Upsert_MatchBy_AllNewEntities_AllInserted()
    {
        using var context = CreateContext();

        var batch = new[]
        {
            new CustomerOrder { OrderNumber = "NEW-1", CustomerName = "A", TotalAmount = 1m, OrderDate = DateTimeOffset.UtcNow },
            new CustomerOrder { OrderNumber = "NEW-2", CustomerName = "B", TotalAmount = 2m, OrderDate = DateTimeOffset.UtcNow },
            new CustomerOrder { OrderNumber = "NEW-3", CustomerName = "C", TotalAmount = 3m, OrderDate = DateTimeOffset.UtcNow }
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.Upsert(batch, new UpsertOptions().WithMatchBy<CustomerOrder, string>(o => o.OrderNumber));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(3);
        result.UpdatedCount.ShouldBe(0);
    }

    [Fact]
    public void Upsert_MatchBy_AllExistingEntities_AllUpdated()
    {
        using var context = CreateContext();
        MatchByTestHelpers.SeedOrder(context, "EX-1", "OldA", 1m);
        MatchByTestHelpers.SeedOrder(context, "EX-2", "OldB", 2m);
        MatchByTestHelpers.SeedOrder(context, "EX-3", "OldC", 3m);

        var batch = new[]
        {
            new CustomerOrder { OrderNumber = "EX-1", CustomerName = "NewA", TotalAmount = 11m, OrderDate = DateTimeOffset.UtcNow },
            new CustomerOrder { OrderNumber = "EX-2", CustomerName = "NewB", TotalAmount = 22m, OrderDate = DateTimeOffset.UtcNow },
            new CustomerOrder { OrderNumber = "EX-3", CustomerName = "NewC", TotalAmount = 33m, OrderDate = DateTimeOffset.UtcNow }
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.Upsert(batch, new UpsertOptions().WithMatchBy<CustomerOrder, string>(o => o.OrderNumber));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(3);
        result.InsertedCount.ShouldBe(0);
    }

    [Fact]
    public void Upsert_MatchBy_EmptyBatch_FiresNoSelect()
    {
        var probe = new SelectProbe();
        using var context = CreateContextWithInterceptor(probe);

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.Upsert(Array.Empty<CustomerOrder>(),
            new UpsertOptions().WithMatchBy<CustomerOrder, string>(o => o.OrderNumber));

        result.SuccessCount.ShouldBe(0);
        result.FailureCount.ShouldBe(0);
        probe.SelectsAgainstCustomerOrders.ShouldBe(0,
            "an empty batch must short-circuit before MatchBy issues a SELECT.");
    }

    [Fact]
    public void Upsert_MatchBy_DuplicateMatchKeys_AllNulls_DoNotThrow()
    {
        using var context = CreateContext();

        // Two entities sharing a null CategoryId — RejectDuplicateMatchKeys skips null tuples,
        // so this must NOT throw the duplicate-match-key error.
        var batch = new[]
        {
            new Product { Name = "A", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow, CategoryId = null },
            new Product { Name = "B", Price = 2m, Stock = 1, LastModified = DateTimeOffset.UtcNow, CategoryId = null }
        };

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert(batch, new UpsertOptions().WithMatchBy<Product, int?>(p => p.CategoryId));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(2);
        result.InsertedWithNullMatchKeyCount.ShouldBe(2);
    }

    [Fact]
    public void UpsertGraphOperation_DoesNotImplement_IMatchByCapableOperation()
    {
        // After 4.3 the interface split, graph upsert deliberately opts out of MatchBy: the
        // operation type must not claim the capability so strategies/retry skip the pre-SELECT
        // and refresh hooks via the as-cast.
        var operationType = typeof(Winnow.Operations.UpsertGraphOperation<CustomerOrder, int>);
        var matchByCapable = typeof(IMatchByCapableOperation<CustomerOrder, int>);

        matchByCapable.IsAssignableFrom(operationType).ShouldBeFalse(
            "UpsertGraphOperation must not implement IMatchByCapableOperation; do not add the stubs back.");
    }

    [Fact]
    public void MatchExpressionParser_ValidateShape_LambdaWithWrongParameterType_Throws()
    {
        // Manually construct a lambda whose parameter type is `string` rather than `Product`.
        var param = Expression.Parameter(typeof(string), "s");
        var body = Expression.Constant("x");
        var lambda = Expression.Lambda<Func<string, string>>(body, param);

        Should.Throw<ArgumentException>(() => MatchExpressionParser.ValidateShape<Product>(lambda))
            .Message.ShouldContain(nameof(Product));
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

    [Fact]
    public void UpsertOperation_DoesNotCarryPerBatchMatchByResolutionAsInstanceState()
    {
        // Per-batch MatchBy resolution must flow through StrategyContext (which has the
        // correct per-execution lifetime) and not as a mutable field on UpsertOperation.
        // Holding resolution on the operation type leaks state between batches if the
        // operation is ever reused, and would silently corrupt routing if the type is
        // shared across concurrent invocations. This is a design-contract test.
        var opType = typeof(Winnow.Operations.UpsertOperation<CustomerOrder, int>);
        var leakingFields = opType
            .GetFields(System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public)
            .Where(f => f.FieldType.IsGenericType
                && f.FieldType.GetGenericTypeDefinition() == typeof(MatchByResolution<>))
            .Select(f => f.Name)
            .ToList();

        leakingFields.ShouldBeEmpty(
            "UpsertOperation must not hold MatchByResolution as instance state. " +
            $"Found leaking field(s): {string.Join(", ", leakingFields)}. " +
            "Move per-batch resolution onto StrategyContext.");
    }

    private sealed class SelectProbe : DbCommandInterceptor
    {
        public int SelectsAgainstCustomerOrders { get; private set; }

        public override InterceptionResult<System.Data.Common.DbDataReader> ReaderExecuting(
            System.Data.Common.DbCommand command,
            CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result)
        {
            var sql = command.CommandText.AsSpan().TrimStart();
            if (sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("CustomerOrders"))
            {
                SelectsAgainstCustomerOrders++;
            }
            return base.ReaderExecuting(command, eventData, result);
        }
    }
}
