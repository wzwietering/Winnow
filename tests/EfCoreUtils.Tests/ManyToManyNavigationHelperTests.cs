using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;

namespace EfCoreUtils.Tests;

/// <summary>
/// Tests for many-to-many navigation detection through public APIs.
/// </summary>
public class ManyToManyNavigationHelperTests : TestBase
{
    [Fact]
    public void SkipNavigation_StudentCourses_DetectedAsManyToMany()
    {
        using var context = CreateContext();

        var student = new Student { Name = "Test", Email = "test@test.com", Courses = [] };
        context.Students.Add(student);
        context.SaveChanges();

        var entry = context.Entry(student);
        var coursesNav = entry.Navigation(nameof(Student.Courses));

        // Verify it's a skip navigation
        coursesNav.Metadata.ShouldBeAssignableTo<ISkipNavigation>();
    }

    [Fact]
    public void SkipNavigation_CourseStudents_DetectedAsManyToMany()
    {
        using var context = CreateContext();

        var course = new Course { Code = "CS101", Title = "Test Course", Credits = 3, Students = [] };
        context.Courses.Add(course);
        context.SaveChanges();

        var entry = context.Entry(course);
        var studentsNav = entry.Navigation(nameof(Course.Students));

        // Verify it's a skip navigation
        studentsNav.Metadata.ShouldBeAssignableTo<ISkipNavigation>();
    }

    [Fact]
    public void ExplicitJoinEntity_Enrollment_HasTwoForeignKeys()
    {
        using var context = CreateContext();

        var enrollmentType = context.Model.FindEntityType(typeof(Enrollment));
        enrollmentType.ShouldNotBeNull();

        var foreignKeys = enrollmentType!.GetForeignKeys().ToList();

        // Enrollment has exactly 2 foreign keys (StudentId, CourseId)
        foreignKeys.Count.ShouldBe(2);

        // Verify they point to different principal types
        var principalTypes = foreignKeys
            .Select(fk => fk.PrincipalEntityType.ClrType)
            .Distinct()
            .ToList();
        principalTypes.Count.ShouldBe(2);
        principalTypes.ShouldContain(typeof(Student));
        principalTypes.ShouldContain(typeof(Course));
    }

    [Fact]
    public void RegularCollection_OrderItems_HasOneForeignKey()
    {
        using var context = CreateContext();

        var orderItemType = context.Model.FindEntityType(typeof(OrderItem));
        orderItemType.ShouldNotBeNull();

        var foreignKeys = orderItemType!.GetForeignKeys().ToList();

        // OrderItem has foreign keys to CustomerOrder and optionally Product
        // but it's not a join entity (not exactly 2 FKs to different principals)
        foreignKeys.Any().ShouldBeTrue();

        // The FK to CustomerOrder exists
        var customerOrderFk = foreignKeys.FirstOrDefault(fk =>
            fk.PrincipalEntityType.ClrType == typeof(CustomerOrder));
        customerOrderFk.ShouldNotBeNull();
    }

    [Fact]
    public void StudentEnrollments_IsCollection_NotSkipNavigation()
    {
        using var context = CreateContext();

        var student = new Student { Name = "Test", Email = "test@test.com", Enrollments = [] };
        context.Students.Add(student);
        context.SaveChanges();

        var entry = context.Entry(student);
        var enrollmentsNav = entry.Navigation(nameof(Student.Enrollments));

        // Verify it's NOT a skip navigation (it's to explicit join entity)
        enrollmentsNav.Metadata.ShouldNotBeAssignableTo<ISkipNavigation>();

        // But it IS a collection
        enrollmentsNav.Metadata.IsCollection.ShouldBeTrue();
    }

    [Fact]
    public void ReferenceNavigation_NotManyToMany()
    {
        using var context = CreateContext();

        var enrollment = new Enrollment
        {
            StudentId = 1,
            CourseId = 1,
            EnrolledAt = DateTime.UtcNow
        };

        var entry = context.Entry(enrollment);
        var studentNav = entry.Navigation(nameof(Enrollment.Student));

        // Reference navigation is not a collection
        studentNav.Metadata.IsCollection.ShouldBeFalse();
    }
}
