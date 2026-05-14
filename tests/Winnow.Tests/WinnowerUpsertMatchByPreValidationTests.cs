using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

public class WinnowerUpsertMatchByPreValidationTests : TestBase
{
    // Bug 1 proof: combining WithValidation + WithMatchBy used to crash with
    // IndexOutOfRangeException when an earlier entity was rejected by pre-validation,
    // because ResolveBatch built EntityMatchValues indexed by survivor position
    // while PrepareEntity read it with the original input index.
    [Fact]
    public void Upsert_MatchByWithPreValidationRejectingFirstEntity_DoesNotCrash()
    {
        using var context = CreateContext();
        MatchByTestHelpers.SeedOrder(context, "EXISTING", "Original", 50m);

        var batch = new[]
        {
            new CustomerOrder { OrderNumber = "BAD", CustomerName = "Bad", TotalAmount = -1m },
            new CustomerOrder { OrderNumber = "EXISTING", CustomerName = "Updated", TotalAmount = 10m },
            new CustomerOrder { OrderNumber = "NEW", CustomerName = "New", TotalAmount = 20m },
        };

        var options = new UpsertOptions().WithMatchBy<CustomerOrder>(o => o.OrderNumber);
        options.WithValidation<CustomerOrder>((CustomerOrder o, ref ValidationCollector c) =>
        {
            if (o.TotalAmount <= 0) c.Add(nameof(CustomerOrder.TotalAmount), "Must be positive");
        });

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.Upsert(batch, options);

        result.FailureCount.ShouldBe(1);
        result.Failures.ShouldHaveSingleItem().EntityIndex.ShouldBe(0);
        result.Failures[0].Reason.ShouldBe(FailureReason.ValidationError);
        result.InsertedCount.ShouldBe(1);
        result.UpdatedCount.ShouldBe(1);
    }

    [Fact]
    public void Upsert_MatchByWithPreValidationRejectingMiddleEntity_RoutesSurvivorsCorrectly()
    {
        using var context = CreateContext();
        MatchByTestHelpers.SeedOrder(context, "EXISTING", "Original", 50m);

        var batch = new[]
        {
            new CustomerOrder { OrderNumber = "EXISTING", CustomerName = "Updated", TotalAmount = 10m },
            new CustomerOrder { OrderNumber = "BAD", CustomerName = "Bad", TotalAmount = -1m },
            new CustomerOrder { OrderNumber = "NEW", CustomerName = "New", TotalAmount = 20m },
        };

        var options = new UpsertOptions().WithMatchBy<CustomerOrder>(o => o.OrderNumber);
        options.WithValidation<CustomerOrder>((CustomerOrder o, ref ValidationCollector c) =>
        {
            if (o.TotalAmount <= 0) c.Add(nameof(CustomerOrder.TotalAmount), "Must be positive");
        });

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.Upsert(batch, options);

        result.FailureCount.ShouldBe(1);
        result.Failures.ShouldHaveSingleItem().EntityIndex.ShouldBe(1);
        result.InsertedCount.ShouldBe(1);
        result.UpdatedCount.ShouldBe(1);
    }

    // Bug 2 proof: when pre-validation rejects entities between two duplicate
    // MatchBy keys, RejectDuplicateMatchKeys iterates the survivor-indexed values[]
    // but its error message claims those positions are caller indices. The user
    // submitted entities 0 and 2; the message must report 0 and 2, not 0 and 1.
    [Fact]
    public void Upsert_MatchByDuplicateKeysAcrossPreValidationFailure_ReportsOriginalInputIndices()
    {
        using var context = CreateContext();

        var batch = new[]
        {
            new CustomerOrder { OrderNumber = "DUP", CustomerName = "First", TotalAmount = 10m },
            new CustomerOrder { OrderNumber = "BAD", CustomerName = "Bad", TotalAmount = -1m },
            new CustomerOrder { OrderNumber = "DUP", CustomerName = "Second", TotalAmount = 20m },
        };

        var options = new UpsertOptions().WithMatchBy<CustomerOrder>(o => o.OrderNumber);
        options.WithValidation<CustomerOrder>((CustomerOrder o, ref ValidationCollector c) =>
        {
            if (o.TotalAmount <= 0) c.Add(nameof(CustomerOrder.TotalAmount), "Must be positive");
        });

        var saver = new Winnower<CustomerOrder, int>(context);
        var ex = Should.Throw<InvalidOperationException>(() => saver.Upsert(batch, options));

        ex.Message.ShouldContain("0 and 2");
    }

    [Fact]
    public async Task UpsertAsync_MatchByWithPreValidationRejectingFirstEntity_DoesNotCrash()
    {
        using var context = CreateContext();
        MatchByTestHelpers.SeedOrder(context, "EXISTING-ASYNC", "Original", 50m);

        var batch = new[]
        {
            new CustomerOrder { OrderNumber = "BAD", CustomerName = "Bad", TotalAmount = -1m },
            new CustomerOrder { OrderNumber = "EXISTING-ASYNC", CustomerName = "Updated", TotalAmount = 10m },
            new CustomerOrder { OrderNumber = "NEW-ASYNC", CustomerName = "New", TotalAmount = 20m },
        };

        var options = new UpsertOptions().WithMatchBy<CustomerOrder>(o => o.OrderNumber);
        options.WithValidation<CustomerOrder>((CustomerOrder o, ref ValidationCollector c) =>
        {
            if (o.TotalAmount <= 0) c.Add(nameof(CustomerOrder.TotalAmount), "Must be positive");
        });

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = await saver.UpsertAsync(batch, options);

        result.FailureCount.ShouldBe(1);
        result.Failures.ShouldHaveSingleItem().EntityIndex.ShouldBe(0);
        result.InsertedCount.ShouldBe(1);
        result.UpdatedCount.ShouldBe(1);
    }

    [Fact]
    public void Upsert_MatchByWithAllEntitiesRejectedByValidation_NoDatabaseWork()
    {
        using var context = CreateContext();
        MatchByTestHelpers.SeedOrder(context, "EXISTING", "Original", 50m);
        var initialCount = context.CustomerOrders.AsNoTracking().Count();

        var batch = new[]
        {
            new CustomerOrder { OrderNumber = "BAD-1", CustomerName = "Bad1", TotalAmount = -1m },
            new CustomerOrder { OrderNumber = "BAD-2", CustomerName = "Bad2", TotalAmount = -2m },
        };

        var options = new UpsertOptions().WithMatchBy<CustomerOrder>(o => o.OrderNumber);
        options.WithValidation<CustomerOrder>((CustomerOrder o, ref ValidationCollector c) =>
        {
            if (o.TotalAmount <= 0) c.Add(nameof(CustomerOrder.TotalAmount), "Must be positive");
        });

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.Upsert(batch, options);

        result.FailureCount.ShouldBe(2);
        result.InsertedCount.ShouldBe(0);
        result.UpdatedCount.ShouldBe(0);
        result.DatabaseRoundTrips.ShouldBe(0);

        context.ChangeTracker.Clear();
        context.CustomerOrders.AsNoTracking().Count().ShouldBe(initialCount);
    }
}
