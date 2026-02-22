namespace Winnow.Tests.Entities;

/// <summary>
/// Explicit join entity with payload properties (EnrolledAt, Grade).
/// Used to test explicit join pattern vs skip navigation pattern.
/// </summary>
public class Enrollment
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public int CourseId { get; set; }
    public DateTime EnrolledAt { get; set; }
    public string? Grade { get; set; }
    public byte[] Version { get; set; } = [];

    public Student Student { get; set; } = null!;
    public Course Course { get; set; } = null!;
}
