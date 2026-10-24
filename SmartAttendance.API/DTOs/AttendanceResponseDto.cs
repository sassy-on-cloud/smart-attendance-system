namespace SmartAttendance.API.DTOs;

public class AttendanceResponseDto
{
    public int Version { get; set; }
    public int? RecordedByUserId { get; set; }
    public int? LastModifiedByUserId { get; set; }
    public DateTime? LastModifiedAtUtc { get; set; }
    public int AttendanceId { get; set; }
    public int StudentId { get; set; }
    public int AttendanceSessionId { get; set; }
    public bool IsLegacy { get; set; }
    public int CourseId { get; set; }
    public DateTime Date { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
}
