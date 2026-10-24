namespace SmartAttendance.API.DTOs;

public class AttendanceResponseDto
{
    public int AttendanceId { get; set; }
    public int StudentId { get; set; }
    public int CourseId { get; set; }
    public DateTime Date { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
}
