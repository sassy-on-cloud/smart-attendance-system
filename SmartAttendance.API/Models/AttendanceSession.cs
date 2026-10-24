namespace SmartAttendance.API.Models;

public class AttendanceSession
{
    public int AttendanceSessionId { get; set; }
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public DateTime? StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public string State { get; set; } = AttendanceSessionStates.Open;
    public int Version { get; set; } = 1;
    public int? CreatedByUserId { get; set; }
    public int? LastModifiedByUserId { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? LastModifiedAtUtc { get; set; }
    public bool IsLegacy { get; set; }
    // Migration-only provenance. A placeholder is not a verified class meeting.
    public int? LegacyAttendanceId { get; set; }
}
