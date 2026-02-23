# Many-to-Many Relationships

Graph operations support many-to-many relationships via skip navigations (EF Core 5+) and explicit join entities.

## Basic Insert with Many-to-Many

```csharp
var saver = new Winnower<Student, int>(context);

var student = new Student
{
    Name = "Alice",
    Courses = existingCourses  // Courses already in database
};

var result = saver.InsertGraph(new[] { student }, new InsertGraphOptions
{
    IncludeManyToMany = true  // Enable M2M handling
});

Console.WriteLine($"Students inserted: {result.SuccessCount}");
Console.WriteLine($"Course enrollments created: {result.TraversalInfo?.JoinRecordsCreated}");
```

## Update with Link Changes

```csharp
var student = context.Students
    .Include(s => s.Courses)
    .First(s => s.Id == 1);

// Modify enrollments
student.Courses.Remove(student.Courses.First());  // Drop a course
student.Courses.Add(newCourse);  // Enroll in new course

var result = saver.UpdateGraph(new[] { student }, new GraphOptions
{
    IncludeManyToMany = true
});

// Check what happened
var stats = result.TraversalInfo?.JoinOperationsByNavigation["Student.Courses"];
Console.WriteLine($"Join records added: {stats?.Created}");
Console.WriteLine($"Join records removed: {stats?.Removed}");
```

## Delete with Join Record Cleanup

```csharp
var studentsToDelete = context.Students
    .Include(s => s.Courses)
    .Where(s => s.GraduationYear < 2020)
    .ToList();

var result = saver.DeleteGraph(studentsToDelete, new DeleteGraphOptions
{
    IncludeManyToMany = true  // Clean up join records
});

// Courses remain in database - only join records removed
Console.WriteLine($"Join records deleted: {result.TraversalInfo?.JoinRecordsRemoved}");
```

## Explicit Join Entity with Payload

For join entities with extra properties (e.g., `EnrolledAt`, `Grade`), modify the join entity collection directly:

```csharp
var enrollment = new Enrollment
{
    StudentId = existingStudent.Id,
    CourseId = existingCourse.Id,
    EnrolledAt = DateTime.UtcNow,
    Grade = null
};

student.Enrollments.Add(enrollment);

var result = saver.UpdateGraph(new[] { student }, new GraphOptions
{
    IncludeManyToMany = true
});
```

## Many-to-Many Options

| Option | Default | Description |
|--------|---------|-------------|
| `IncludeManyToMany` | `false` | Include many-to-many navigations in traversal |
| `ManyToManyInsertBehavior` | `AttachExisting` | `AttachExisting` (assume related entities exist) or `InsertIfNew` (insert if default ID) |
| `ValidateManyToManyEntitiesExist` | `true` | Validate related entities exist before creating join records |

**Note:** If using a `NavigationFilter` that includes a many-to-many navigation (e.g., `Student.Courses`), you must also set `IncludeManyToMany = true`. Otherwise, an `InvalidOperationException` is thrown at operation start. See [Navigation Filtering](graph-operations.md#navigation-filtering) for details.

## Tracking Join Operations

After a graph operation, you can inspect what happened to join records:

```csharp
var traversalInfo = result.TraversalInfo;

// Total counts
Console.WriteLine($"Join records created: {traversalInfo?.JoinRecordsCreated}");
Console.WriteLine($"Join records removed: {traversalInfo?.JoinRecordsRemoved}");

// Per-navigation breakdown
foreach (var (navName, stats) in traversalInfo?.JoinOperationsByNavigation ?? [])
{
    Console.WriteLine($"{navName}: +{stats.Created} / -{stats.Removed}");
}
```
