using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

public class WinnowerUpsertMatchByTests : TestBase
{
    [Fact]
    public void Upsert_MatchBy_SingleMember_NewEntity_Inserted()
    {
        using var context = CreateContext();

        var order = new CustomerOrder
        {
            OrderNumber = "ORD-NEW-1",
            CustomerId = 1,
            CustomerName = "Alice",
            TotalAmount = 100m
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.Upsert(
            new[] { order },
            new UpsertOptions().WithMatchBy<CustomerOrder, string>(o => o.OrderNumber));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);
        result.UpdatedCount.ShouldBe(0);
        order.Id.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Upsert_MatchBy_SingleMember_ExistingEntity_UpdatedByBusinessKey()
    {
        using var context = CreateContext();
        var existing = MatchByTestHelpers.SeedOrder(context, "ORD-100", "Original", 50m);

        var incoming = new CustomerOrder
        {
            Id = 0,
            OrderNumber = "ORD-100",
            CustomerId = 7,
            CustomerName = "Updated",
            TotalAmount = 999m
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.Upsert(
            new[] { incoming },
            new UpsertOptions().WithMatchBy<CustomerOrder, string>(o => o.OrderNumber));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(1);
        result.InsertedCount.ShouldBe(0);

        context.ChangeTracker.Clear();
        var reloaded = context.CustomerOrders.Single(o => o.OrderNumber == "ORD-100");
        reloaded.Id.ShouldBe(existing.Id);
        reloaded.CustomerName.ShouldBe("Updated");
        reloaded.TotalAmount.ShouldBe(999m);
    }

    [Fact]
    public void Upsert_MatchBy_PopulatesPkFromExistingRow_WhenInputHasDefaultPk()
    {
        using var context = CreateContext();
        var existing = MatchByTestHelpers.SeedOrder(context, "ORD-200", "Seed", 10m);

        var incoming = new CustomerOrder
        {
            OrderNumber = "ORD-200",
            CustomerId = 1,
            CustomerName = "Replacement",
            TotalAmount = 20m
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.Upsert(
            new[] { incoming },
            new UpsertOptions().WithMatchBy<CustomerOrder, string>(o => o.OrderNumber));

        result.IsCompleteSuccess.ShouldBeTrue();
        incoming.Id.ShouldBe(existing.Id);
    }

    [Fact]
    public void Upsert_MatchBy_MixedBatch_PartitionsCorrectly()
    {
        using var context = CreateContext();
        MatchByTestHelpers.SeedOrder(context, "ORD-MIX-A", "Existing A", 1m);
        MatchByTestHelpers.SeedOrder(context, "ORD-MIX-B", "Existing B", 2m);

        var batch = new[]
        {
            new CustomerOrder { OrderNumber = "ORD-MIX-A", CustomerName = "Updated A", TotalAmount = 10m },
            new CustomerOrder { OrderNumber = "ORD-MIX-NEW-1", CustomerName = "New 1", TotalAmount = 30m },
            new CustomerOrder { OrderNumber = "ORD-MIX-B", CustomerName = "Updated B", TotalAmount = 20m },
            new CustomerOrder { OrderNumber = "ORD-MIX-NEW-2", CustomerName = "New 2", TotalAmount = 40m },
        };

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.Upsert(
            batch,
            new UpsertOptions().WithMatchBy<CustomerOrder, string>(o => o.OrderNumber));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(2);
        result.InsertedCount.ShouldBe(2);
    }

    [Fact]
    public void Upsert_MatchBy_NullBusinessKey_TreatedAsInsert()
    {
        using var context = CreateContext();

        var product = new Product
        {
            Name = "NullCategoryProduct",
            CategoryId = null,
            Price = 5m,
            Stock = 1,
            LastModified = DateTimeOffset.UtcNow
        };

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert(
            new[] { product },
            new UpsertOptions().WithMatchBy<Product, int?>(p => p.CategoryId));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);
        result.UpdatedCount.ShouldBe(0);
        product.Id.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Upsert_MatchBy_CompositeAnonymous_NewEntity_Inserted()
    {
        using var context = CreateContext();
        SeedStudentAndCourse(context, studentId: 1, courseId: 1);

        var enrollment = new Enrollment
        {
            StudentId = 1,
            CourseId = 1,
            EnrolledAt = DateTime.UtcNow,
            Grade = "A"
        };

        var saver = new Winnower<Enrollment, int>(context);
        var result = saver.Upsert(
            new[] { enrollment },
            new UpsertOptions().WithMatchBy<Enrollment, object>(e => new { e.StudentId, e.CourseId }));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(1);
        enrollment.Id.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Upsert_MatchBy_CompositeAnonymous_ExistingEntity_Updated()
    {
        using var context = CreateContext();
        SeedStudentAndCourse(context, studentId: 7, courseId: 11);
        var seeded = new Enrollment { StudentId = 7, CourseId = 11, Grade = "C", EnrolledAt = DateTime.UtcNow };
        context.Enrollments.Add(seeded);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var incoming = new Enrollment
        {
            StudentId = 7,
            CourseId = 11,
            Grade = "A+",
            EnrolledAt = DateTime.UtcNow
        };

        var saver = new Winnower<Enrollment, int>(context);
        var result = saver.Upsert(
            new[] { incoming },
            new UpsertOptions().WithMatchBy<Enrollment, object>(e => new { e.StudentId, e.CourseId }));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(1);
        incoming.Id.ShouldBe(seeded.Id);

        context.ChangeTracker.Clear();
        var reloaded = context.Enrollments.Single(e => e.StudentId == 7 && e.CourseId == 11);
        reloaded.Grade.ShouldBe("A+");
    }

    [Fact]
    public void Upsert_MatchBy_DivideAndConquerStrategy_PartitionsCorrectly()
    {
        using var context = CreateContext();
        MatchByTestHelpers.SeedOrder(context, "DC-A", "Existing A", 1m);
        MatchByTestHelpers.SeedOrder(context, "DC-B", "Existing B", 2m);

        var batch = new[]
        {
            new CustomerOrder { OrderNumber = "DC-NEW-1", CustomerName = "N1", TotalAmount = 10m },
            new CustomerOrder { OrderNumber = "DC-A", CustomerName = "U1", TotalAmount = 11m },
            new CustomerOrder { OrderNumber = "DC-NEW-2", CustomerName = "N2", TotalAmount = 12m },
            new CustomerOrder { OrderNumber = "DC-B", CustomerName = "U2", TotalAmount = 13m },
        };

        var options = new UpsertOptions { Strategy = BatchStrategy.DivideAndConquer }
            .WithMatchBy<CustomerOrder, string>(o => o.OrderNumber);
        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.Upsert(batch, options);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(2);
        result.UpdatedCount.ShouldBe(2);
    }

    [Fact]
    public void Upsert_MatchByNull_BehavesAsBefore_RegressionGuard()
    {
        using var context = CreateContext();
        SeedData(context, 3);

        var existing = context.Products.AsNoTracking().ToList();
        existing[0].Price = 999m;
        var brandNew = new Product { Name = "Brand New", Price = 7m, Stock = 1, LastModified = DateTimeOffset.UtcNow };
        var batch = new[] { existing[0], existing[1], brandNew };

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert(batch);  // No MatchBy set

        result.IsCompleteSuccess.ShouldBeTrue();
        result.UpdatedCount.ShouldBe(2);
        result.InsertedCount.ShouldBe(1);
    }

    [Fact]
    public void Upsert_MatchBy_WithNullMatchValue_RoutesToInsert_AndReportsNullMatchKeyInsertCount()
    {
        using var context = CreateContext();

        var batch = new[]
        {
            // CategoryId is null — match values contain null, must route to INSERT and be counted.
            new Product { Name = "OrphanA", Price = 1m, Stock = 1, LastModified = DateTimeOffset.UtcNow, CategoryId = null },
            new Product { Name = "OrphanB", Price = 2m, Stock = 1, LastModified = DateTimeOffset.UtcNow, CategoryId = null }
        };

        var saver = new Winnower<Product, int>(context);
        var result = saver.Upsert(
            batch,
            new UpsertOptions().WithMatchBy<Product, int?>(p => p.CategoryId));

        result.IsCompleteSuccess.ShouldBeTrue();
        result.InsertedCount.ShouldBe(2);
        result.UpdatedCount.ShouldBe(0);
        result.NullMatchKeyInsertCount.ShouldBe(2,
            "entities whose MatchBy values contain null are routed to INSERT — the count surfaces that fact for observability.");
    }

    private static void SeedStudentAndCourse(TestDbContext context, int studentId, int courseId)
    {
        context.Students.Add(new Student { Id = studentId, Name = $"Student {studentId}", Email = $"s{studentId}@x.io" });
        context.Courses.Add(new Course { Id = courseId, Code = $"C{courseId}", Title = $"Course {courseId}" });
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }
}
