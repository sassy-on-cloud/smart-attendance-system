namespace SmartAttendance.API.Models;

public class AttendanceSessionHistory
{
    public long AttendanceSessionHistoryId { get; set; }
    public int AttendanceSessionId { get; set; }
    public AttendanceSession Session { get; set; } = null!;
    public int ActorUserId { get; set; }
    public User Actor { get; set; } = null!;
    public DateTime ChangedAtUtc { get; set; }
    public string? PreviousState { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? PreviousStartsAtUtc { get; set; }
    public DateTime? PreviousEndsAtUtc { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int Version { get; set; }
}

public static class AttendanceSessionStates
{
    public const string Open = "Open";
    public const string Closed = "Closed";
    public const string Cancelled = "Cancelled";
}
