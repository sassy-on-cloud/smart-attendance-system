namespace SmartAttendance.API.Models;

public static class AttendanceStatuses
{
    public const string Present = "Present";
    public const string Absent = "Absent";
    public const string Late = "Late";
    public const string Excused = "Excused";
    public const string Unverified = "Unverified"; // Historical value cannot be classified.
}

public static class AttendanceMethods
{
    public const string Manual = "Manual";
    public const string FaceRecognition = "FaceRecognition"; // Vocabulary only; no AI implementation/permission.
    public const string LegacyUnknown = "LegacyUnknown";
}
