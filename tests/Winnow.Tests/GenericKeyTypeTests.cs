using Winnow.Tests.Entities;
using Winnow.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Winnow.Tests;

public class GenericKeyTypeTests : TestBase
{
    [Fact]
    public void Insert_WithIntKey_GeneratesKeys()
    {
        using var context = CreateContext();

        var products = Enumerable.Range(1, 3).Select(i => new Product
        {
            Name = $"Int Product {i}",
            Price = 10.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var saver = new Winnower<Product, int>(context);
        var result = saver.Insert(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.InsertedIds.ShouldAllBe(id => id > 0);
    }

    [Fact]
    public void Insert_WithLongKey_GeneratesKeys()
    {
        using var context = CreateContext();

        var products = Enumerable.Range(1, 3).Select(i => new ProductLong
        {
            Name = $"Long Product {i}",
            Price = 10.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var saver = new Winnower<ProductLong, long>(context);
        var result = saver.Insert(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.InsertedIds.ShouldAllBe(id => id > 0);
    }

    [Fact]
    public void Insert_WithGuidKey_GeneratesKeys()
    {
        using var context = CreateContext();

        var products = Enumerable.Range(1, 3).Select(i => new ProductGuid
        {
            Id = Guid.NewGuid(),
            Name = $"Guid Product {i}",
            Price = 10.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var saver = new Winnower<ProductGuid, Guid>(context);
        var result = saver.Insert(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.InsertedIds.ShouldAllBe(id => id != Guid.Empty);
    }

    [Fact]
    public void Insert_WithStringKey_UsesProvidedKeys()
    {
        using var context = CreateContext();

        var products = Enumerable.Range(1, 3).Select(i => new ProductString
        {
            Id = $"PROD-{i:D4}",
            Name = $"String Product {i}",
            Price = 10.00m + i,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        var saver = new Winnower<ProductString, string>(context);
        var result = saver.Insert(products);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        result.InsertedIds.ShouldContain("PROD-0001");
        result.InsertedIds.ShouldContain("PROD-0002");
        result.InsertedIds.ShouldContain("PROD-0003");
    }

    [Fact]
    public void Update_WithGuidKey_TracksSuccessfulIds()
    {
        using var context = CreateContext();

        // Insert some products first
        var products = Enumerable.Range(1, 3).Select(i => new ProductGuid
        {
            Id = Guid.NewGuid(),
            Name = $"Guid Product {i}",
            Price = 10.00m,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        context.ProductGuids.AddRange(products);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        // Reload and update
        var productsToUpdate = context.ProductGuids.ToList();
        var expectedIds = productsToUpdate.Select(p => p.Id).ToList();
        foreach (var product in productsToUpdate)
        {
            product.Price += 5.00m;
        }

        var saver = new Winnower<ProductGuid, Guid>(context);
        var result = saver.Update(productsToUpdate);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        foreach (var expectedId in expectedIds)
        {
            result.SuccessfulIds.ShouldContain(expectedId);
        }
    }

    [Fact]
    public void Delete_WithLongKey_TracksDeletedIds()
    {
        using var context = CreateContext();

        // Insert some products first
        var products = Enumerable.Range(1, 3).Select(i => new ProductLong
        {
            Name = $"Long Product {i}",
            Price = 10.00m,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        }).ToList();

        context.ProductLongs.AddRange(products);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        // Reload and delete
        var productsToDelete = context.ProductLongs.ToList();
        var expectedIds = productsToDelete.Select(p => p.Id).ToList();

        var saver = new Winnower<ProductLong, long>(context);
        var result = saver.Delete(productsToDelete);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);
        foreach (var expectedId in expectedIds)
        {
            result.SuccessfulIds.ShouldContain(expectedId);
        }

        // Verify deleted
        context.ChangeTracker.Clear();
        context.ProductLongs.Count().ShouldBe(0);
    }

    [Fact]
    public void Winnower_WithWrongKeyType_ThrowsDescriptiveError()
    {
        using var context = CreateContext();

        // Insert a product with int key
        var product = new Product
        {
            Name = "Int Product",
            Price = 10.00m,
            Stock = 100,
            LastModified = DateTimeOffset.UtcNow
        };
        context.Products.Add(product);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var productToUpdate = context.Products.First();
        productToUpdate.Price += 5.00m;

        // Try to use Winnower<Product, long> instead of Winnower<Product, int>
        var saver = new Winnower<Product, long>(context);
        var ex = Should.Throw<InvalidOperationException>(() => saver.Update([productToUpdate]));

        ex.Message.ShouldContain("Primary key type mismatch");
        ex.Message.ShouldContain("Product");
        ex.Message.ShouldContain("Int64"); // long
        ex.Message.ShouldContain("Int32"); // int
    }

    #region Many-to-Many with Int Key

    [Fact]
    public void InsertGraph_IntKey_ManyToMany_ExtractsKeysCorrectly()
    {
        using var context = CreateContext();

        var courses = Enumerable.Range(1, 3).Select(i => new Course
        {
            Code = $"INT{i:D3}",
            Title = $"Int Key Course {i}",
            Credits = 3
        }).ToList();

        var student = new Student
        {
            Name = "Int Key Test",
            Email = "intkey@test.com",
            Courses = courses
        };

        var saver = new Winnower<Student, int>(context);
        var result = saver.InsertGraph([student], new InsertGraphOptions
        {
            IncludeManyToMany = true,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.InsertIfNew
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo!.JoinRecordsCreated.ShouldBe(3);

        // Verify int keys were generated correctly
        context.ChangeTracker.Clear();
        var loaded = context.Students
            .Include(s => s.Courses)
            .First(s => s.Name == "Int Key Test");

        loaded.Id.ShouldBeGreaterThan(0);
        loaded.Courses.ShouldAllBe(c => c.Id > 0);
    }

    [Fact]
    public void UpdateGraph_IntKey_ManyToMany_UpdatesCorrectly()
    {
        using var context = CreateContext();

        // First insert student with initial course
        var initialCourse = new Course { Code = "UPD000", Title = "Initial Course", Credits = 3 };
        var newCourse = new Course { Code = "UPD001", Title = "New Course", Credits = 3 };
        context.Courses.AddRange(initialCourse, newCourse);
        context.SaveChanges();

        var student = new Student
        {
            Name = "Update Test",
            Email = "update@test.com",
            Courses = [initialCourse]
        };
        context.Students.Add(student);
        context.SaveChanges();
        var studentId = student.Id;
        var newCourseId = newCourse.Id;
        context.ChangeTracker.Clear();

        // Load student with courses, add new course, remove initial
        var loaded = context.Students.Include(s => s.Courses).First(s => s.Id == studentId);
        var courseToAdd = context.Courses.First(c => c.Id == newCourseId);
        loaded.Courses.Clear();
        loaded.Courses.Add(courseToAdd);

        var saver = new Winnower<Student, int>(context);
        var result = saver.UpdateGraph([loaded], new GraphOptions
        {
            IncludeManyToMany = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        // Verify database state is correct - the M2M update worked
        context.ChangeTracker.Clear();
        var verified = context.Students.Include(s => s.Courses).First(s => s.Id == studentId);
        verified.Courses.Count.ShouldBe(1);
        verified.Courses.First().Id.ShouldBe(newCourseId);

        // Note: Statistics tracking for skip navigation updates is limited because
        // the original state cannot be captured after user modifies the collection.
        // EF Core handles the actual update correctly via its change tracker.
    }

    [Fact]
    public void DeleteGraph_IntKey_ManyToMany_HandlesKeysCorrectly()
    {
        using var context = CreateContext();

        // Create student with courses
        var courses = Enumerable.Range(1, 2).Select(i => new Course
        {
            Code = $"DEL{i:D3}",
            Title = $"Delete Test {i}",
            Credits = 3
        }).ToList();
        context.Courses.AddRange(courses);
        context.SaveChanges();

        var student = new Student { Name = "Delete Test", Email = "delete@test.com" };
        context.Students.Add(student);
        context.SaveChanges();

        // Add enrollments
        foreach (var course in courses)
        {
            context.Enrollments.Add(new Enrollment
            {
                StudentId = student.Id,
                CourseId = course.Id,
                EnrolledAt = DateTime.UtcNow
            });
        }
        context.SaveChanges();
        var studentId = student.Id;
        context.ChangeTracker.Clear();

        // Delete student (should cascade delete enrollments)
        var loaded = context.Students.Include(s => s.Enrollments).First(s => s.Id == studentId);

        var saver = new Winnower<Student, int>(context);
        var result = saver.DeleteGraph([loaded], new DeleteGraphOptions
        {
            IncludeManyToMany = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        // Verify student and enrollments deleted, but courses remain
        context.ChangeTracker.Clear();
        context.Students.Count(s => s.Id == studentId).ShouldBe(0);
        context.Enrollments.Count(e => e.StudentId == studentId).ShouldBe(0);
        context.Courses.Count().ShouldBe(2); // Courses should remain
    }

    #endregion
}
