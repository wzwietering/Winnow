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
            new UpsertOptions().WithMatchBy<CustomerOrder, string>(o => o.OrderNumber));

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
            new UpsertOptions().WithMatchBy<Enrollment, object>(e => new { e.StudentId, e.CourseId }));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(1);

        context.ChangeTracker.Clear();
        var reloaded = context.Enrollments.Single();
        reloaded.Grade.ShouldBe("A");
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
            new UpsertOptions().WithMatchBy<CustomerOrder, string>(o => o.OrderNumber),
            cts.Token));
    }

}
