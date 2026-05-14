using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

public class WinnowerUpsertMatchByPrecedenceTests : TestBase
{
    [Fact]
    public void MatchBy_OverridesHasDefaultKeyValue_WhenInputPkPopulatedButRowDoesNotExistInDb()
    {
        using var context = CreateContext();

        // No row exists. Input has a non-default PK (legacy behavior would treat as UPDATE
        // and fail because the row doesn't exist). With MatchBy, the lookup misses,
        // so the entity is routed to INSERT — using the caller-supplied PK.
        var order = new CustomerOrder
        {
            Id = 4242,
            OrderNumber = "ORD-NOWHERE",
            CustomerId = 1,
            CustomerName = "Anyone",
            TotalAmount = 1m
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.Upsert(
            new[] { order },
            new UpsertOptions().WithMatchBy<CustomerOrder>(o => o.OrderNumber));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);
        result.UpdatedCount.ShouldBe(0);

        context.ChangeTracker.Clear();
        var reloaded = context.CustomerOrders.Single(o => o.OrderNumber == "ORD-NOWHERE");
        reloaded.Id.ShouldBe(4242);
    }

    [Fact]
    public void MatchBy_ReplacesInputPkWithResolvedPkOnUpdate()
    {
        using var context = CreateContext();
        var seeded = new CustomerOrder
        {
            OrderNumber = "ORD-REPLACE",
            CustomerId = 1,
            CustomerName = "Seed",
            TotalAmount = 1m,
            OrderDate = DateTimeOffset.UtcNow
        };
        context.CustomerOrders.Add(seeded);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var incoming = new CustomerOrder
        {
            Id = 9999,
            OrderNumber = "ORD-REPLACE",
            CustomerId = 2,
            CustomerName = "Replacement",
            TotalAmount = 5m
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.Upsert(
            new[] { incoming },
            new UpsertOptions().WithMatchBy<CustomerOrder>(o => o.OrderNumber));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(1);
        incoming.Id.ShouldBe(seeded.Id);  // Input PK was overwritten by resolved PK.
        incoming.Id.ShouldNotBe(9999);
    }
}
