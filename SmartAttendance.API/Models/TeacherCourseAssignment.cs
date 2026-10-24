namespace SmartAttendance.API.Models;

public class TeacherCourseAssignment
{
    public int TeacherUserId { get; set; }
    public User Teacher { get; set; } = null!;
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
}
