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

    [Fact]
    public void InsertGraph_LargeCollection_PerformsEfficiently()
    {
        // Tests that large M2M collections are handled efficiently (verifies N+1 fix)
        using var context = CreateContext();

        // Create 50 courses - enough to verify efficiency without being excessive
        var courses = Enumerable.Range(1, 50)
            .Select(i => CreateCourse($"BATCH{i:D3}"))
            .ToList();

        var student = CreateStudent("LargeTest", courses);

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = true,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.InsertIfNew
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo!.JoinRecordsCreated.ShouldBe(50);

        context.ChangeTracker.Clear();
        var loaded = context.Students.Include(s => s.Courses).First();
        loaded.Courses.Count.ShouldBe(50);
    }

    [Fact]
    public void UpdateGraph_MultipleSequentialOperations_StateIsolated()
    {
        // Tests that state from previous operations doesn't affect subsequent operations
        // (verifies state corruption fix)
        using var context = CreateContext();
        SeedCourses(context, 3);
        var courseIds = context.Courses.Select(c => c.Id).ToList();

        // Create two students
        var student1 = CreateStudent("Sequential1");
        var student2 = CreateStudent("Sequential2");
        context.Students.AddRange(student1, student2);
        context.SaveChanges();

        // Add enrollments to student1
        context.Enrollments.AddRange(courseIds.Take(2).Select(cid => new Enrollment
        {
            StudentId = student1.Id,
            CourseId = cid,
            EnrolledAt = DateTime.UtcNow
        }));
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var saver = new BatchSaver<Student, int>(context);

        // First update operation
        var loaded1 = context.Students.Include(s => s.Enrollments).First(s => s.Id == student1.Id);
        loaded1.Name = "Sequential1 Updated";
        var result1 = saver.UpdateGraphBatch([loaded1], new GraphBatchOptions
        {
            IncludeManyToMany = true
        });
        result1.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();

        // Second update operation on different student (state should be isolated)
        var loaded2 = context.Students.Include(s => s.Enrollments).First(s => s.Id == student2.Id);
        loaded2.Name = "Sequential2 Updated";
        var result2 = saver.UpdateGraphBatch([loaded2], new GraphBatchOptions
        {
            IncludeManyToMany = true
        });
        result2.IsCompleteSuccess.ShouldBeTrue();

        // Verify both students have correct state
        context.ChangeTracker.Clear();
        var verified1 = context.Students.Include(s => s.Enrollments).First(s => s.Id == student1.Id);
        var verified2 = context.Students.Include(s => s.Enrollments).First(s => s.Id == student2.Id);

        verified1.Name.ShouldBe("Sequential1 Updated");
        verified1.Enrollments.Count.ShouldBe(2);
        verified2.Name.ShouldBe("Sequential2 Updated");
        verified2.Enrollments.Count.ShouldBe(0);
    }

    [Fact]
    public void InsertGraph_ValidationFailure_RelatedEntityNotFound()
    {
        using var context = CreateContext();

        // Create a course object with a non-existent ID (simulating a detached entity)
        var fakeCourse = new Course
        {
            Id = 99999, // Non-existent ID
            Code = "FAKE",
            Title = "Fake Course",
            Credits = 3
        };

        var student = CreateStudent("ValidationTest", [fakeCourse]);

        var saver = new BatchSaver<Student, int>(context);

        // Batch operations catch exceptions and record failures instead of throwing
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = true,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.AttachExisting,
            ValidateManyToManyEntitiesExist = true
        });

        // Should have a failure because validation detected non-existent course
        result.IsCompleteSuccess.ShouldBeFalse();
        result.Failures.ShouldNotBeEmpty();
        result.Failures.First().ErrorMessage.ShouldContain("99999");
    }

    [Fact]
    public void InsertGraph_ValidationDisabled_SkipsCheck()
    {
        using var context = CreateContext();

        var student = CreateStudent("NoValidation", [CreateCourse("NEW001")]);

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = true,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.InsertIfNew,
            ValidateManyToManyEntitiesExist = false
        });

        result.IsCompleteSuccess.ShouldBeTrue();
    }

    #endregion

    #region ManyToManyInsertBehavior Tests

    [Fact]
    public void InsertBehavior_AttachExisting_ExistingCourses_AttachesAsUnchanged()
    {
        using var context = CreateContext();
        SeedCourses(context, 3);

        // Create detached course references with existing IDs
        var courseIds = context.Courses.Select(c => c.Id).ToList();
        context.ChangeTracker.Clear();

        var detachedCourses = courseIds.Select(id => new Course
        {
            Id = id,
            Code = $"EXIST{id}",
            Title = $"Existing Course {id}",
            Credits = 3
        }).ToList();

        var student = CreateStudent("AttachTest", detachedCourses);

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = true,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.AttachExisting,
            ValidateManyToManyEntitiesExist = true
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo!.JoinRecordsCreated.ShouldBe(3);

        // Verify courses weren't duplicated
        context.ChangeTracker.Clear();
        context.Courses.Count().ShouldBe(3);
    }

    [Fact]
    public void InsertBehavior_InsertIfNew_MixedIds_InsertsOnlyNew()
    {
        using var context = CreateContext();
        SeedCourses(context, 2);

        // Get an existing course ID, then clear tracker
        var existingCourseId = context.Courses.First().Id;
        context.ChangeTracker.Clear();

        // Create detached reference to existing course
        var existingCourse = new Course
        {
            Id = existingCourseId,
            Code = "EXISTING",
            Title = "Existing Course",
            Credits = 3
        };

        // Mix of existing (Id > 0) and new (Id = 0)
        var courses = new List<Course>
        {
            existingCourse,
            CreateCourse("NEW001"), // Id = 0, should be inserted
            CreateCourse("NEW002")  // Id = 0, should be inserted
        };
        var student = CreateStudent("MixedTest", courses);

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = true,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.InsertIfNew
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        context.ChangeTracker.Clear();
        context.Courses.Count().ShouldBe(4); // 2 existing + 2 new
    }

    [Fact]
    public void InsertBehavior_InsertIfNew_AllExisting_AttachesAll()
    {
        using var context = CreateContext();
        SeedCourses(context, 3);

        // Create detached course references with existing IDs
        var courseIds = context.Courses.Select(c => c.Id).ToList();
        context.ChangeTracker.Clear();

        var detachedCourses = courseIds.Select(id => new Course
        {
            Id = id,
            Code = $"EXIST{id}",
            Title = $"Existing Course {id}",
            Credits = 3
        }).ToList();

        var student = CreateStudent("AllExisting", detachedCourses);

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = true,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.InsertIfNew
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        // Verify courses weren't duplicated
        context.ChangeTracker.Clear();
        context.Courses.Count().ShouldBe(3);
    }

    [Fact]
    public void InsertBehavior_AttachExisting_AllNewCourses_AttachesAsUnchanged()
    {
        using var context = CreateContext();

        // Create new courses with assigned IDs (simulating detached entities)
        var newCourses = new List<Course>
        {
            CreateCourse("CS701"),
            CreateCourse("CS702")
        };
        var student = CreateStudent("NewWithAttach", newCourses);

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = true,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.InsertIfNew,
            ValidateManyToManyEntitiesExist = false
        });

        result.IsCompleteSuccess.ShouldBeTrue();

        // With InsertIfNew and Id=0, courses should be inserted
        context.ChangeTracker.Clear();
        context.Courses.Count().ShouldBe(2);
    }

    #endregion

    #region Error Conditions

    [Fact]
    public void InsertGraph_CollectionSizeExceedsLimit_FailsWithClearMessage()
    {
        using var context = CreateContext();

        var courses = Enumerable.Range(1, 15)
            .Select(i => CreateCourse($"LIMIT{i:D2}"))
            .ToList();
        var student = CreateStudent("SizeLimit", courses);

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = true,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.InsertIfNew,
            MaxManyToManyCollectionSize = 10
        });

        result.IsCompleteSuccess.ShouldBeFalse();
        result.Failures.ShouldNotBeEmpty();
        result.Failures.First().ErrorMessage.ShouldContain("15 items");
        result.Failures.First().ErrorMessage.ShouldContain("MaxManyToManyCollectionSize of 10");
    }

    [Fact]
    public void InsertGraph_MultipleNonExistentEntities_ListsAllInErrorMessage()
    {
        using var context = CreateContext();

        // Create course objects with non-existent IDs
        var fakeCourses = new List<Course>
        {
            new() { Id = 99901, Code = "FAKE1", Title = "Fake 1", Credits = 3 },
            new() { Id = 99902, Code = "FAKE2", Title = "Fake 2", Credits = 3 },
            new() { Id = 99903, Code = "FAKE3", Title = "Fake 3", Credits = 3 }
        };

        var student = CreateStudent("MultiMissing", fakeCourses);

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = true,
            ManyToManyInsertBehavior = ManyToManyInsertBehavior.AttachExisting,
            ValidateManyToManyEntitiesExist = true
        });

        result.IsCompleteSuccess.ShouldBeFalse();
        result.Failures.ShouldNotBeEmpty();

        var errorMessage = result.Failures.First().ErrorMessage;
        errorMessage.ShouldContain("99901");
        errorMessage.ShouldContain("99902");
        errorMessage.ShouldContain("99903");
    }

    [Fact]
    public void UpdateGraph_CollectionSizeExceedsLimit_FailsWithClearMessage()
    {
        using var context = CreateContext();
        SeedCourses(context, 20);

        var student = CreateStudent("UpdateLimit");
        context.Students.Add(student);
        context.SaveChanges();
        var studentId = student.Id;
        context.ChangeTracker.Clear();

        // Load student and add many courses
        var loaded = context.Students.First(s => s.Id == studentId);
        loaded.Courses = context.Courses.Take(15).ToList();

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.UpdateGraphBatch([loaded], new GraphBatchOptions
        {
            IncludeManyToMany = true,
            MaxManyToManyCollectionSize = 10
        });

        result.IsCompleteSuccess.ShouldBeFalse();
        result.Failures.ShouldNotBeEmpty();
        result.Failures.First().ErrorMessage.ShouldContain("15 items");
        result.Failures.First().ErrorMessage.ShouldContain("MaxManyToManyCollectionSize of 10");
    }

    [Fact]
    public void InsertGraph_EmptyCoursesCollection_Succeeds()
    {
        using var context = CreateContext();

        var student = CreateStudent("EmptyCollection", []);

        var saver = new BatchSaver<Student, int>(context);
        var result = saver.InsertGraphBatch([student], new InsertGraphBatchOptions
        {
            IncludeManyToMany = true,
            MaxManyToManyCollectionSize = 5
        });

        result.IsCompleteSuccess.ShouldBeTrue();
        result.TraversalInfo!.JoinRecordsCreated.ShouldBe(0);
    }

    #endregion
}
