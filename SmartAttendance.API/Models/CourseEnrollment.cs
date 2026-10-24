namespace SmartAttendance.API.Models;

public class CourseEnrollment
{
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    // Historical attendance is evidence, not confirmation of current enrollment.
    public bool IsLegacyInferred { get; set; }
}
