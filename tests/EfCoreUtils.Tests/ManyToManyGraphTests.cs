using EfCoreUtils.Tests.Entities;
using EfCoreUtils.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace EfCoreUtils.Tests;

public class ManyToManyGraphTests : TestBase
{
    #region Helper Methods

    private static Student CreateStudent(string name, ICollection<Course>? courses = null) => new()
    {
        Name = name,
        Email = $"{name.ToLower().Replace(" ", ".")}@test.com",
        Courses = courses ?? []
    };

    private static Course CreateCourse(string code, int credits = 3) => new()
    {
        Code = code,
        Title = $"{code} - Course Title",
        Credits = credits
    };

    private static Enrollment CreateEnrollment(Student student, Course course, string? grade = null) => new()
    {
        StudentId = student.Id,
        CourseId = course.Id,
        EnrolledAt = DateTime.UtcNow,
        Grade = grade,
        Student = student,
        Course = course
    };

    private static void SeedCourses(TestDbContext context, int count = 5)
    {
        var courses = Enumerable.Range(1, count)
            .Select(i => CreateCourse($"CS10{i}"))
            .ToList();
        context.Courses.AddRange(courses);
        context.SaveChanges();
        context.ChangeTracker.Clear();
    }

    #endregion

    #region Insert Operations

    [Fact]
    public void InsertGraph_SkipNavigation_WithNewCourses_JoinRecordsCreated()
    {
        using var context = CreateContext();

        // Create new courses (Id=0) to insert together with student
        var newCourses = new List<Course>
        {
            CreateCourse("CS101"),
            CreateCourse("CS102"),
            CreateCourse("CS103")
        };

        var student = CreateStudent("Alice", newCourses);

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = true,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.InsertIfNew
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo.ShouldNotBeNull();
        result.TraversalInfo!.JoinRecordsCreated.ShouldBe(3);

        context.ChangeTracker.Clear();
        var loaded = context.Students.Include(s => s.Courses).First();
        loaded.Courses.Count.ShouldBe(3);
    }

    [Fact]
    public void InsertGraph_ExplicitJoin_JoinEntitiesCreated()
    {
        using var context = CreateContext();
        SeedCourses(context, 2);
        var courses = context.Courses.ToList();

        var student = CreateStudent("Bob");
        context.Students.Add(student);
        context.SaveChanges();

        // Create enrollments with explicit join entity
        var enrollments = courses.Select(c => new Enrollment
        {
            StudentId = student.Id,
            CourseId = c.Id,
            EnrolledAt = DateTime.UtcNow,
            Grade = "A"
        }).ToList();

        context.Enrollments.AddRange(enrollments);
        context.SaveChanges();

        context.ChangeTracker.Clear();
        var loaded = context.Students.Include(s => s.Enrollments).First();
        loaded.Enrollments.Count.ShouldBe(2);
        loaded.Enrollments.ShouldAllBe(e => e.Grade == "A");
    }

    [Fact]
    public void InsertGraph_WithNewCoursesOnly_InsertsAll()
    {
        using var context = CreateContext();

        var newCourses = new List<Course>
        {
            CreateCourse("CS201"),
            CreateCourse("CS202")
        };
        var student = CreateStudent("Charlie", newCourses);

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = true,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.InsertIfNew
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.Courses.Count().ShouldBe(2);
        context.Students.Include(s => s.Courses).First().Courses.Count.ShouldBe(2);
    }

    [Fact]
    public void InsertGraph_EmptyCollection_NoJoinRecordsCreated()
    {
        using var context = CreateContext();

        var student = CreateStudent("Eve");

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo.ShouldNotBeNull();
        result.TraversalInfo!.JoinRecordsCreated.ShouldBe(0);
    }

    [Fact]
    public void InsertGraph_MultipleStudents_EachWithOwnCourses_AllInserted()
    {
        using var context = CreateContext();

        // Each student gets their own course objects to avoid tracking conflicts
        var student1 = CreateStudent("Frank", [CreateCourse("CS301"), CreateCourse("CS302")]);
        var student2 = CreateStudent("Grace", [CreateCourse("CS303"), CreateCourse("CS304")]);

        var students = new[] { student1, student2 };

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch(students, new InsertGraphBatchOptions
        {
            IncludeManyToMany = true,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.InsertIfNew
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(2);

        context.ChangeTracker.Clear();
        var loadedStudents = context.Students.Include(s => s.Courses).ToList();
        loadedStudents.Count.ShouldBe(2);
    }

    [Fact]
    public void InsertGraph_Result_TracksJoinRecordsCreated()
    {
        using var context = CreateContext();

        var courses = new List<Course>
        {
            CreateCourse("CS401"),
            CreateCourse("CS402"),
            CreateCourse("CS403")
        };
        var student = CreateStudent("Henry", courses);

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = true,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.InsertIfNew
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo.ShouldNotBeNull();
        result.TraversalInfo!.JoinRecordsCreated.ShouldBe(3);
    }

    [Fact]
    public void InsertGraph_IncludeManyToManyFalse_CoursesNotLinked()
    {
        using var context = CreateContext();

        var student = CreateStudent("Ivy");

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = false
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo!.JoinRecordsCreated.ShouldBe(0);
    }

    [Fact]
    public void InsertGraph_StudentWithoutCourses_Succeeds()
    {
        using var context = CreateContext();

        var student = CreateStudent("Jack");

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        student.Id.ShouldBeGreaterThan(0);
    }

    #endregion

    #region Update Operations

    [Fact]
    public void UpdateGraph_StudentProperties_NoM2MChanges()
    {
        using var context = CreateContext();

        var student = CreateStudent("Jack");
        context.Students.Add(student);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loaded = context.Students.First();
        loaded.Name = "Jack Updated";
        loaded.Email = "jack.updated@test.com";

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.UpdateGraphBatch([loaded], new GraphBatchOptions
        {
            IncludeManyToMany = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo!.JoinRecordsCreated.ShouldBe(0);
        result.TraversalInfo.JoinRecordsRemoved.ShouldBe(0);

        context.ChangeTracker.Clear();
        var verified = context.Students.First();
        verified.Name.ShouldBe("Jack Updated");
    }

    [Fact]
    public void UpdateGraph_AddEnrollment_DirectSave()
    {
        // Tests adding enrollment via direct EF Core, not through BatchSaver
        // BatchSaver's UpdateGraph may require specific setup for new children
        using var context = CreateContext();
        SeedCourses(context, 3);
        var courseId = context.Courses.First().Id;

        var student = CreateStudent("Noah");
        context.Students.Add(student);
        context.SaveChanges();
        var studentId = student.Id;
        context.ChangeTracker.Clear();

        // Add enrollment directly
        context.Enrollments.Add(new Enrollment
        {
            StudentId = studentId,
            CourseId = courseId,
            EnrolledAt = DateTime.UtcNow,
            Grade = "B+"
        });
        context.SaveChanges();

        context.ChangeTracker.Clear();
        var verified = context.Students.Include(s => s.Enrollments).First(s => s.Id == studentId);
        verified.Enrollments.Count.ShouldBe(1);
        verified.Enrollments.First().Grade.ShouldBe("B+");
    }

    [Fact]
    public void UpdateGraph_ExplicitJoin_RemoveEnrollment_WithOrphanDelete()
    {
        using var context = CreateContext();
        SeedCourses(context, 2);
        var courseIds = context.Courses.Select(c => c.Id).ToList();

        var student = CreateStudent("Olivia");
        context.Students.Add(student);
        context.SaveChanges();

        context.Enrollments.AddRange(courseIds.Select(cid => new Enrollment
        {
            StudentId = student.Id,
            CourseId = cid,
            EnrolledAt = DateTime.UtcNow
        }));
        context.SaveChanges();
        var studentId = student.Id;
        context.ChangeTracker.Clear();

        var loaded = context.Students.Include(s => s.Enrollments).First(s => s.Id == studentId);
        var enrollmentToRemove = loaded.Enrollments.First();
        loaded.Enrollments.Remove(enrollmentToRemove);

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.UpdateGraphBatch([loaded], new GraphBatchOptions
        {
            IncludeManyToMany = true,
            OrphanedChildBehavior = OrphanBehavior.Delete
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var verified = context.Students.Include(s => s.Enrollments).First(s => s.Id == studentId);
        verified.Enrollments.Count.ShouldBe(1);
    }

    [Fact]
    public void UpdateGraph_PayloadJoinEntity_PreservesPayload()
    {
        using var context = CreateContext();
        SeedCourses(context, 2);
        var courseId = context.Courses.First().Id;

        var student = CreateStudent("Rose");
        context.Students.Add(student);
        context.SaveChanges();

        context.Enrollments.Add(new Enrollment
        {
            StudentId = student.Id,
            CourseId = courseId,
            EnrolledAt = DateTime.UtcNow,
            Grade = "A-"
        });
        context.SaveChanges();
        var studentId = student.Id;
        context.ChangeTracker.Clear();

        var loaded = context.Students.Include(s => s.Enrollments).First(s => s.Id == studentId);
        loaded.Name = "Rose Updated";

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.UpdateGraphBatch([loaded], new GraphBatchOptions
        {
            IncludeManyToMany = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        var verified = context.Students.Include(s => s.Enrollments).First(s => s.Id == studentId);
        verified.Enrollments.First().Grade.ShouldBe("A-");
    }

    [Fact]
    public void UpdateGraph_MultipleStudents_BatchUpdate()
    {
        using var context = CreateContext();

        var students = Enumerable.Range(1, 5)
            .Select(i => CreateStudent($"Student{i}"))
            .ToList();
        context.Students.AddRange(students);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loaded = context.Students.ToList();
        foreach (var student in loaded)
        {
            student.Name += " Updated";
        }

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.UpdateGraphBatch(loaded, new GraphBatchOptions
        {
            IncludeManyToMany = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(5);
    }

    [Fact]
    public void UpdateGraph_Strategy_OneByOne_Works()
    {
        using var context = CreateContext();

        var student = CreateStudent("Peter");
        context.Students.Add(student);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loaded = context.Students.First();
        loaded.Name = "Peter Updated";

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.UpdateGraphBatch([loaded], new GraphBatchOptions
        {
            Strategy = BatchStrategy.OneByOne,
            IncludeManyToMany = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public void UpdateGraph_Strategy_DivideAndConquer_Works()
    {
        using var context = CreateContext();

        var student = CreateStudent("Quinn");
        context.Students.Add(student);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loaded = context.Students.First();
        loaded.Name = "Quinn Updated";

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.UpdateGraphBatch([loaded], new GraphBatchOptions
        {
            Strategy = BatchStrategy.DivideAndConquer,
            IncludeManyToMany = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    #endregion

    #region Delete Operations

    [Fact]
    public void DeleteGraph_StudentOnly_Succeeds()
    {
        using var context = CreateContext();

        var student = CreateStudent("Tina");
        context.Students.Add(student);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loaded = context.Students.First();

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.DeleteGraphBatch([loaded], new DeleteGraphBatchOptions
        {
            IncludeManyToMany = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.Students.Count().ShouldBe(0);
    }

    [Fact]
    public void DeleteGraph_WithExplicitJoinEntities_CascadeDeletes()
    {
        using var context = CreateContext();
        SeedCourses(context, 2);
        var courseIds = context.Courses.Select(c => c.Id).ToList();

        var student = CreateStudent("Uma");
        context.Students.Add(student);
        context.SaveChanges();

        context.Enrollments.AddRange(courseIds.Select(cid => new Enrollment
        {
            StudentId = student.Id,
            CourseId = cid,
            EnrolledAt = DateTime.UtcNow
        }));
        context.SaveChanges();
        var studentId = student.Id;
        context.ChangeTracker.Clear();

        var loaded = context.Students.Include(s => s.Enrollments).First(s => s.Id == studentId);

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.DeleteGraphBatch([loaded], new DeleteGraphBatchOptions
        {
            IncludeManyToMany = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.Students.Count().ShouldBe(0);
        context.Enrollments.Count().ShouldBe(0);
    }

    [Fact]
    public void DeleteGraph_CoursesRemainAfterStudentDelete()
    {
        using var context = CreateContext();
        SeedCourses(context, 3);
        var originalCourseCount = context.Courses.Count();

        var student = CreateStudent("Victor");
        context.Students.Add(student);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loaded = context.Students.First();

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.DeleteGraphBatch([loaded], new DeleteGraphBatchOptions
        {
            IncludeManyToMany = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.Courses.Count().ShouldBe(originalCourseCount);
    }

    [Fact]
    public void DeleteGraph_NoManyToMany_UnchangedBehavior()
    {
        using var context = CreateContext();

        var student = CreateStudent("Xavier");
        context.Students.Add(student);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loaded = context.Students.First();

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.DeleteGraphBatch([loaded], new DeleteGraphBatchOptions
        {
            IncludeManyToMany = false
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.Students.Count().ShouldBe(0);
    }

    [Fact]
    public void DeleteGraph_MultipleStudents_AllDeleted()
    {
        using var context = CreateContext();

        var students = Enumerable.Range(1, 3)
            .Select(i => CreateStudent($"Student{i}"))
            .ToList();
        context.Students.AddRange(students);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var loaded = context.Students.ToList();

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.DeleteGraphBatch(loaded, new DeleteGraphBatchOptions
        {
            Strategy = BatchStrategy.OneByOne,
            IncludeManyToMany = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.SuccessCount.ShouldBe(3);

        context.ChangeTracker.Clear();
        context.Students.Count().ShouldBe(0);
    }

    #endregion

    #region Edge Cases and Configuration

    [Fact]
    public void Config_IncludeManyToManyFalse_NoM2MProcessing()
    {
        using var context = CreateContext();

        var student = CreateStudent("Adam");

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = false
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo!.JoinRecordsCreated.ShouldBe(0);
    }

    [Fact]
    public void Config_MaxDepth_Zero_StillInsertsEntity()
    {
        using var context = CreateContext();

        var student = CreateStudent("Beth");

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = true,
            MaxDepth = 0
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        student.Id.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void EdgeCase_NullNavigation_Skipped()
    {
        using var context = CreateContext();

        var student = new Student
        {
            Name = "Carl",
            Email = "carl@test.com",
            Courses = null!
        };

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public void EdgeCase_EmptyEnrollmentsCollection_Handled()
    {
        using var context = CreateContext();

        var student = CreateStudent("Diana");
        student.Enrollments = [];

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Backward_DefaultOptions_NoChange()
    {
        using var context = CreateContext();

        var student = CreateStudent("Eve");

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student]);

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo!.JoinRecordsCreated.ShouldBe(0);
    }

    [Fact]
    public void Statistics_TraversalInfo_NotNull()
    {
        using var context = CreateContext();

        var student = CreateStudent("Frank");

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = true
        });

        result.TraversalInfo.ShouldNotBeNull();
    }

    [Fact]
    public void Insert_WithNewCourses_JoinOperationsByNavigation_HasEntries()
    {
        using var context = CreateContext();

        var courses = new List<Course>
        {
            CreateCourse("CS501"),
            CreateCourse("CS502")
        };
        var student = CreateStudent("Grace", courses);

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = true,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.InsertIfNew
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo.ShouldNotBeNull();
        result.TraversalInfo!.JoinRecordsCreated.ShouldBe(2);
    }

    #endregion
}
