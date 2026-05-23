using Microsoft.EntityFrameworkCore;
using Shouldly;
using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;

namespace Winnow.Tests;

public class WinnowerGraphValidationTests : TestBase
{
    private static ValidatorDelegate<CustomerOrder> RejectOrderNumber(string orderNumber)
        => (CustomerOrder o, ref ValidationCollector c) =>
        {
            if (o.OrderNumber == orderNumber)
                c.Add(nameof(CustomerOrder.OrderNumber), "Rejected");
        };

    private static CustomerOrder NewOrder(string orderNumber, int itemCount = 1) => new()
    {
        OrderNumber = orderNumber,
        CustomerName = "Test",
        CustomerId = 1,
        TotalAmount = 1m,
        OrderDate = DateTimeOffset.UtcNow,
        OrderItems = Enumerable.Range(1, itemCount).Select(i => new OrderItem
        {
            ProductId = 100 + i,
            ProductName = $"P{i}",
            Quantity = 1,
            UnitPrice = 1m,
            Subtotal = 1m,
        }).ToList(),
    };

    [Fact]
    public void InsertGraph_PreValidationRejectsParent_GraphIsNotInserted()
    {
        using var context = CreateContext();
        var orders = new[] { NewOrder("OK"), NewOrder("BAD") };

        var options = new InsertGraphOptions();
        options.WithValidation(RejectOrderNumber("BAD"));

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.InsertGraph(orders, options);

        result.SuccessCount.ShouldBe(1);
        result.FailureCount.ShouldBe(1);
        var failure = result.Failures.ShouldHaveSingleItem();
        failure.EntityIndex.ShouldBe(1);
        failure.Reason.ShouldBe(FailureReason.ValidationError);

        // BAD order not in DB; OK order is.
        context.ChangeTracker.Clear();
        context.CustomerOrders.Count(o => o.OrderNumber == "BAD").ShouldBe(0);
        context.CustomerOrders.Count(o => o.OrderNumber == "OK").ShouldBe(1);
    }

    [Fact]
    public void UpdateGraph_PreValidationRejectsParent_GraphIsNotUpdated()
    {
        using var context = CreateContext();
        var order = NewOrder("ORIG");
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        order.OrderNumber = "BAD";

        var options = new GraphOptions();
        options.WithValidation(RejectOrderNumber("BAD"));

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpdateGraph([order], options);

        result.FailureCount.ShouldBe(1);
        result.Failures.ShouldHaveSingleItem().Reason.ShouldBe(FailureReason.ValidationError);

        context.ChangeTracker.Clear();
        context.CustomerOrders.AsNoTracking().Single().OrderNumber.ShouldBe("ORIG");
    }

    [Fact]
    public void DeleteGraph_PreValidationRejectsParent_NotDeleted()
    {
        using var context = CreateContext();
        var order = NewOrder("KEEP-ME");
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var attached = context.CustomerOrders.AsNoTracking().Include(o => o.OrderItems).Single();

        var options = new DeleteGraphOptions();
        options.WithValidation(RejectOrderNumber("KEEP-ME"));

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.DeleteGraph([attached], options);

        result.FailureCount.ShouldBe(1);
        result.Failures.ShouldHaveSingleItem().Reason.ShouldBe(FailureReason.ValidationError);

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(1);
    }

    [Fact]
    public void UpsertGraph_PreValidationFailure_RecordsAsFailure()
    {
        using var context = CreateContext();
        var orders = new[] { NewOrder("OK"), NewOrder("BAD") };

        var options = new UpsertGraphOptions();
        options.WithValidation(RejectOrderNumber("BAD"));

        var saver = new Winnower<CustomerOrder, int>(context);
        var result = saver.UpsertGraph(orders, options);

        result.SuccessCount.ShouldBe(1);
        result.FailureCount.ShouldBe(1);
        result.Failures.ShouldHaveSingleItem().Reason.ShouldBe(FailureReason.ValidationError);
        context.ChangeTracker.Clear();
        context.CustomerOrders.Count(o => o.OrderNumber == "BAD").ShouldBe(0);
        context.CustomerOrders.Count(o => o.OrderNumber == "OK").ShouldBe(1);
    }

    [Fact]
    public void UpdateGraph_ThrowBehavior_ThrowsWinnowValidationException()
    {
        using var context = CreateContext();
        var order = NewOrder("ORIG");
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        order.OrderNumber = "BAD";

        var options = new GraphOptions()
            .WithValidation(RejectOrderNumber("BAD"), ValidationFailureBehavior.Throw);

        var saver = new Winnower<CustomerOrder, int>(context);
        var ex = Should.Throw<WinnowValidationException>(() => saver.UpdateGraph([order], options));
        ex.Failures.ShouldHaveSingleItem().EntityIndex.ShouldBe(0);

        context.ChangeTracker.Clear();
        context.CustomerOrders.AsNoTracking().Single().OrderNumber.ShouldBe("ORIG");
    }

    [Fact]
    public void DeleteGraph_ThrowBehavior_ThrowsWinnowValidationException()
    {
        using var context = CreateContext();
        var order = NewOrder("KEEP-ME");
        context.CustomerOrders.Add(order);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var attached = context.CustomerOrders.AsNoTracking().Include(o => o.OrderItems).Single();

        var options = new DeleteGraphOptions()
            .WithValidation(RejectOrderNumber("KEEP-ME"), ValidationFailureBehavior.Throw);

        var saver = new Winnower<CustomerOrder, int>(context);
        Should.Throw<WinnowValidationException>(() => saver.DeleteGraph([attached], options));

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(1);
    }

    [Fact]
    public void UpsertGraph_ThrowBehavior_ThrowsWinnowValidationException()
    {
        using var context = CreateContext();
        var orders = new[] { NewOrder("OK"), NewOrder("BAD") };

        var options = new UpsertGraphOptions()
            .WithValidation(RejectOrderNumber("BAD"), ValidationFailureBehavior.Throw);

        var saver = new Winnower<CustomerOrder, int>(context);
        var ex = Should.Throw<WinnowValidationException>(() => saver.UpsertGraph(orders, options));
        ex.Failures.ShouldHaveSingleItem().EntityIndex.ShouldBe(1);

        context.ChangeTracker.Clear();
        context.CustomerOrders.Count().ShouldBe(0);
    }

    // Locks the many-to-many + validation intersection. Student.Courses is an
    // EF Core skip navigation: a custom validator runs on the parent and may
    // inspect the navigation collection. The join-table writes should only
    // happen for survivors of pre-validation — rejected students must produce
    // no rows in StudentCourse. Without this test the M2M code path went
    // through pre-validation untested.
    [Fact]
    public void InsertGraph_ManyToMany_PreValidationRejectsParent_NoJoinRowsWritten()
    {
        using var context = CreateContext();

        ValidatorDelegate<Student> rejectEmptyCourseList = (Student s, ref ValidationCollector c) =>
        {
            if (s.Courses.Count == 0) c.Add(nameof(Student.Courses), "Must enroll in at least one course", "EMPTY");
        };

        var students = new[]
        {
            new Student { Name = "Alice", Email = "a@t", Courses = [new Course { Code = "C1", Title = "T1", Credits = 1 }] },
            new Student { Name = "Bob", Email = "b@t", Courses = [] }, // rejected
        };

        var options = new InsertGraphOptions
        {
            IncludeManyToMany = true,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.InsertIfNew,
        };
        options.WithValidation(rejectEmptyCourseList);

        var saver = new Winnower<Student, int>(context);
        var result = saver.InsertGraph(students, options);

        result.SuccessCount.ShouldBe(1);
        result.FailureCount.ShouldBe(1);
        var failure = result.Failures.ShouldHaveSingleItem();
        failure.EntityIndex.ShouldBe(1);
        failure.Reason.ShouldBe(FailureReason.ValidationError);
        failure.ValidationErrors!.ShouldContain(e => e.Code == "EMPTY");

        context.ChangeTracker.Clear();
        context.Students.Count(s => s.Name == "Bob").ShouldBe(0);
        var alice = context.Students.Include(s => s.Courses).Single(s => s.Name == "Alice");
        alice.Courses.Count.ShouldBe(1);
        alice.Courses.Single().Code.ShouldBe("C1");
    }
}
