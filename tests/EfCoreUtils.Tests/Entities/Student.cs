namespace EfCoreUtils.Tests.Entities;

public class Student
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public byte[] Version { get; set; } = [];

    // Skip navigation (EF Core manages implicit join table StudentCourse)
    public ICollection<Course> Courses { get; set; } = [];

    // Explicit join navigation (for payload testing - EnrolledAt, Grade)
    public ICollection<Enrollment> Enrollments { get; set; } = [];
}
