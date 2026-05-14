using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

/// <summary>
/// Verifies the ergonomic <see cref="UpsertOptionsExtensions.WithMatchBy{TEntity}"/> overload
/// that omits the explicit TKey type argument. Composite-key MatchBy with the original
/// two-parameter overload required spelling out <c>TKey = object</c>, which is unintuitive.
/// </summary>
public class WinnowerUpsertMatchByErgonomicsTests : TestBase
{
    [Fact]
    public void WithMatchBy_CompositeKey_NoExplicitTKey_Compiles()
    {
        var options = new UpsertOptions();

        // Single type argument; no need for TKey = object on a composite anonymous projection.
        var result = options.WithMatchBy<CustomerOrder>(o => new { o.OrderNumber, o.CustomerId });

        result.ShouldBeSameAs(options);
        result.MatchBy.ShouldNotBeNull();
    }

    [Fact]
    public void WithMatchBy_CalledTwice_SecondExpressionWins()
    {
        // Locks in the documented "last call wins" behavior. Replacing this with
        // a throw later would be a breaking API change.
        using var context = CreateContext();
        var existing = MatchByTestHelpers.SeedOrder(context, "WIN-1", "Original-By-OrderNumber", 10m);
        var saver = new Winnower<CustomerOrder, int>(context);

        var incoming = new CustomerOrder
        {
            OrderNumber = "WIN-1",
            CustomerId = 1,
            CustomerName = "Original-By-OrderNumber",
            TotalAmount = 99m,
            OrderDate = DateTimeOffset.UtcNow
        };

        // First call would route by OrderNumber (would UPDATE the seeded row); second
        // call replaces with CustomerName routing (no row matches "MISMATCH", so INSERT).
        // Different OrderNumbers on the seed and incoming guarantee the discriminator
        // forces a unique routing decision.
        var options = new UpsertOptions()
            .WithMatchBy<CustomerOrder>(o => o.OrderNumber)
            .WithMatchBy<CustomerOrder>(o => o.CustomerName);

        var result = saver.Upsert(new[] { incoming }, options);

        result.UpdatedCount.ShouldBe(1,
            "Second WithMatchBy must replace the first; CustomerName routing should match the seeded row.");
        result.InsertedCount.ShouldBe(0);
    }

    [Fact]
    public void WithMatchBy_CompositeKey_NoExplicitTKey_RoutesUpsert()
    {
        using var context = CreateContext();
        var saver = new Winnower<CustomerOrder, int>(context);

        var existing = new CustomerOrder
        {
            OrderNumber = "ORD-1",
            CustomerId = 1,
            CustomerName = "Original",
            TotalAmount = 10m,
            OrderDate = DateTimeOffset.UtcNow
        };
        context.CustomerOrders.Add(existing);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var incoming = new CustomerOrder
        {
            OrderNumber = "ORD-1",
            CustomerId = 1,
            CustomerName = "Updated",
            TotalAmount = 99m,
            OrderDate = DateTimeOffset.UtcNow
        };

        var options = new UpsertOptions()
            .WithMatchBy<CustomerOrder>(o => new { o.OrderNumber, o.CustomerId });

        var result = saver.Upsert(new[] { incoming }, options);

        result.UpdatedCount.ShouldBe(1);
        result.InsertedCount.ShouldBe(0);
    }
}
