using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

public class WinnowerUpsertMatchByAsyncTests : TestBase
{
    [Fact]
    public async Task UpsertAsync_MatchBy_MixedBatch_PartitionsCorrectly()
    {
        using var context = CreateContext();
        MatchByTestHelpers.SeedOrder(context, "ASYNC-A", "Existing", 1m);

        var batch = new[]
        {
            new CustomerOrder { OrderNumber = "ASYNC-A", CustomerName = "Updated", TotalAmount = 10m },
            new CustomerOrder { OrderNumber = "ASYNC-NEW", CustomerName = "New", TotalAmount = 20m }
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = await saver.UpsertAsync(
            batch,
            new UpsertOptions().WithMatchBy<CustomerOrder>(o => o.OrderNumber));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);
        result.UpdatedCount.ShouldBe(1);
    }

    [Fact]
    public async Task UpsertAsync_MatchBy_Composite_PartitionsCorrectly()
    {
        using var context = CreateContext();
        context.Students.Add(new Student { Id = 1, Name = "S", Email = "s@x.io" });
        context.Courses.Add(new Course { Id = 1, Code = "C", Title = "C" });
        await context.SaveChangesAsync();
        context.Enrollments.Add(new Enrollment { StudentId = 1, CourseId = 1, Grade = "B", EnrolledAt = DateTime.UtcNow });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var batch = new[]
        {
            new Enrollment { StudentId = 1, CourseId = 1, Grade = "A", EnrolledAt = DateTime.UtcNow }
        };

        var saver = new Winnower<Enrollment, int>(context);
        var result = await saver.UpsertAsync(
            batch,
            new UpsertOptions().WithMatchBy<Enrollment>(e => new { e.StudentId, e.CourseId }));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        var reloaded = context.Enrollments.Single();
        reloaded.Grade.ShouldBe("A");
    }

    [Fact]
    public async Task UpsertAsync_MatchBy_EntityWithCompositePrimaryKey_PopulatesAllPkColumnsFromExistingRow()
    {
        // Async analogue of the sync test in WinnowerUpsertMatchByBehavioralCoverageTests.
        using var context = CreateContext();
        var seededAt = new DateTime(2026, 5, 14, 11, 0, 0, DateTimeKind.Utc);
        context.InventoryLocations.Add(new InventoryLocation
        {
            WarehouseCode = "ASYNC-WH",
            AisleNumber = 7,
            BinCode = "B-99",
            Quantity = 5,
            LastUpdated = seededAt
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var update = new InventoryLocation { Quantity = 42, LastUpdated = seededAt };
        var saver = new Winnower<InventoryLocation, CompositeKey>(context);
        var result = await saver.UpsertAsync(
            new[] { update },
            new UpsertOptions().WithMatchBy<InventoryLocation>(i => i.LastUpdated));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(1);
        update.WarehouseCode.ShouldBe("ASYNC-WH");
        update.AisleNumber.ShouldBe(7);
        update.BinCode.ShouldBe("B-99");
    }

    [Fact]
    public async Task UpsertAsync_MatchBy_OneByOneStrategy_MixedBatch_PartitionsCorrectly()
    {
        // Async analogue of the sync OneByOne coverage in WinnowerUpsertMatchByBehavioralCoverageTests.
        using var context = CreateContext();
        MatchByTestHelpers.SeedOrder(context, "OBO-ASYNC-EXISTING", "Existing", 100m);

        var batch = new[]
        {
            new CustomerOrder { OrderNumber = "OBO-ASYNC-EXISTING", CustomerName = "Updated", TotalAmount = 150m },
            new CustomerOrder { OrderNumber = "OBO-ASYNC-NEW", CustomerName = "New", TotalAmount = 75m }
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = await saver.UpsertAsync(
            batch,
            new UpsertOptions { Strategy = BatchStrategy.OneByOne }
                .WithMatchBy<CustomerOrder>(o => o.OrderNumber));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(1);
        result.InsertedCount.ShouldBe(1);
    }

    [Fact]
    public async Task UpsertAsync_MatchBy_WithNullMatchValue_ReportsInsertedWithNullMatchKeyCount()
    {
        // Async analogue of the sync test in WinnowerUpsertMatchByTests; single-instance
        // (non-parallel) reporting of the null-key count through the async pipeline.
        using var context = CreateContext();
        var batch = new[]
        {
            new Product { Name = "OrphanAsync", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow, CategoryId = null }
        };

        var saver = new Winnower<Product, int>(context);
        var result = await saver.UpsertAsync(
            batch,
            new UpsertOptions().WithMatchBy<Product>(p => p.CategoryId));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);
        result.InsertedWithNullMatchKeyCount.ShouldBe(1,
            "async upsert must populate InsertedWithNullMatchKeyCount the same as sync.");
    }

    [Fact]
    public async Task UpsertAsync_MatchBy_PreCancelled_NoWork()
    {
        using var context = CreateContext();
        var batch = new[] { new CustomerOrder { OrderNumber = "PRE-CANCEL", CustomerName = "X", TotalAmount = 1m } };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var saver = new Winnower<CustomerOrder, int>(context);

        await Should.ThrowAsync<OperationCanceledException>(async () => await saver.UpsertAsync(
            batch,
            new UpsertOptions().WithMatchBy<CustomerOrder>(o => o.OrderNumber),
            cts.Token));
    }

}
