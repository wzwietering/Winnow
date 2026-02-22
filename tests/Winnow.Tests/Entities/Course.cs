namespace Winnow.Tests.Entities;

public class Course
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Credits { get; set; }
    public byte[] Version { get; set; } = [];

    // Skip navigation (inverse of Student.Courses)
    public ICollection<Student> Students { get; set; } = [];

    // Explicit join navigation
    public ICollection<Enrollment> Enrollments { get; set; } = [];
}
