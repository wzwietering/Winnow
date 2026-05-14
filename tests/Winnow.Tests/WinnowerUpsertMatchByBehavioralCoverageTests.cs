using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

/// <summary>
/// Behavioral coverage tests closing gaps identified during the 1.2.0 deep review.
/// These exercise production code paths that had no assertions: concurrency-token
/// copying, composite primary keys, the OneByOne strategy, and direct-instance
/// key reads on entities without change-tracker presence.
/// </summary>
public class WinnowerUpsertMatchByBehavioralCoverageTests : TestBase
{
    [Fact]
    public void Upsert_MatchBy_OnUpdate_CopiesConcurrencyTokenFromExistingRow()
    {
        // Without the copy, the input entity's Version would be empty when EF flips
        // it to Modified, causing the UPDATE's WHERE-by-rowversion to match zero rows
        // and EF to throw DbUpdateConcurrencyException. A successful update proves
        // CopyConcurrencyTokensFromExisting copied the seeded row's Version.
        using var context = CreateContext();
        context.CustomerOrders.Add(new CustomerOrder
        {
            OrderNumber = "TOKEN-COPY",
            CustomerId = 1,
            CustomerName = "Original",
            TotalAmount = 100m,
            OrderDate = DateTimeOffset.UtcNow
        });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var update = new CustomerOrder
        {
            // Id = 0 (default), Version = [] (empty) — the copy is what makes UPDATE succeed.
            OrderNumber = "TOKEN-COPY",
            CustomerId = 1,
            CustomerName = "Updated",
            TotalAmount = 200m,
            OrderDate = DateTimeOffset.UtcNow
        };
        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.Upsert(
            new[] { update },
            new UpsertOptions().WithMatchBy<CustomerOrder>(o => o.OrderNumber));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(1);
        result.InsertedCount.ShouldBe(0);
    }

    [Fact]
    public void Upsert_MatchBy_EntityWithCompositePrimaryKey_PopulatesAllPkColumnsFromExistingRow()
    {
        // InventoryLocation has a 3-column composite PK (WarehouseCode, AisleNumber, BinCode).
        // MatchBy on LastUpdated (a non-PK column, unique within this test's seed) routes
        // to update; the existing row's full composite PK must be copied onto the input.
        using var context = CreateContext();
        var seededAt = new DateTime(2026, 5, 14, 10, 0, 0, DateTimeKind.Utc);
        context.InventoryLocations.Add(new InventoryLocation
        {
            WarehouseCode = "WH-1",
            AisleNumber = 3,
            BinCode = "B-42",
            Quantity = 10,
            LastUpdated = seededAt
        });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var update = new InventoryLocation
        {
            // No PK columns supplied — MatchBy on LastUpdated must populate all three.
            Quantity = 99,
            LastUpdated = seededAt
        };
        var saver = new Winnower<InventoryLocation, CompositeKey>(context);
        var result = saver.Upsert(
            new[] { update },
            new UpsertOptions().WithMatchBy<InventoryLocation>(i => i.LastUpdated));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(1);
        update.WarehouseCode.ShouldBe("WH-1");
        update.AisleNumber.ShouldBe(3);
        update.BinCode.ShouldBe("B-42");
    }

    [Fact]
    public void Upsert_MatchBy_OneByOneStrategy_MixedBatch_PartitionsCorrectly()
    {
        // BatchStrategy.OneByOne wires up ResolveBatch the same way as DivideAndConquer
        // but iterates entities individually. Existing MatchBy tests only exercise
        // DivideAndConquer; this one closes the OneByOne coverage gap.
        using var context = CreateContext();
        context.CustomerOrders.Add(new CustomerOrder
        {
            OrderNumber = "OBO-EXISTING",
            CustomerId = 1,
            CustomerName = "Existing",
            TotalAmount = 100m,
            OrderDate = DateTimeOffset.UtcNow
        });
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var batch = new[]
        {
            new CustomerOrder
            {
                OrderNumber = "OBO-EXISTING",
                CustomerId = 1,
                CustomerName = "Existing-updated",
                TotalAmount = 150m,
                OrderDate = DateTimeOffset.UtcNow
            },
            new CustomerOrder
            {
                OrderNumber = "OBO-NEW",
                CustomerId = 2,
                CustomerName = "New",
                TotalAmount = 75m,
                OrderDate = DateTimeOffset.UtcNow
            }
        };
        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.Upsert(
            batch,
            new UpsertOptions { Strategy = BatchStrategy.OneByOne }
                .WithMatchBy<CustomerOrder>(o => o.OrderNumber));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(1);
        result.InsertedCount.ShouldBe(1);
    }

    [Fact]
    public void EntityKeyService_GetEntityIdFromInstance_NullPrimaryKey_ThrowsWithEntityName()
    {
        // MatchBy reads PK via reflection (bypassing the change tracker). The null-PK
        // guard in EntityKeyService.ReadSimpleKey has no direct coverage today; without
        // it, MatchBy on entities with null reference-type PKs would NRE deep inside
        // the routing path with no entity-name context.
        using var context = CreateContext();
        var keyService = new Winnow.Internal.Services.EntityKeyService<ProductString, string>(context);
        var entity = new ProductString { Id = null!, Name = "x", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow };

        var ex = Should.Throw<InvalidOperationException>(() =>
            keyService.GetEntityIdFromInstance(entity));
        ex.Message.ShouldContain(nameof(ProductString));
    }
}
