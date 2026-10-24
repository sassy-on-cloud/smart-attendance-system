namespace SmartAttendance.API.Models;

public class AttendanceHistory
{
    public long AttendanceHistoryId { get; set; }
    public int AttendanceId { get; set; }
    public Attendance Attendance { get; set; } = null!;
    public int ActorUserId { get; set; }
    public User Actor { get; set; } = null!;
    public DateTime ChangedAtUtc { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public string? PreviousStatus { get; set; }
    public string? PreviousMethod { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int Version { get; set; }
}
