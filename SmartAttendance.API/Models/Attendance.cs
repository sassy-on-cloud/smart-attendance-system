namespace SmartAttendance.API.Models
{
    public class Attendance
    {
        public int AttendanceId { get; set; }

        public int StudentId { get; set; }

        public Student? Student { get; set; }

        public int CourseId { get; set; }

        public Course? Course { get; set; }

        public int AttendanceSessionId { get; set; }

        public AttendanceSession Session { get; set; } = null!;

        public CourseEnrollment Enrollment { get; set; } = null!;

        // Raw pre-session values are retained for audit/reconciliation, not API input.
        public string? LegacyStatus { get; set; }

        public string? LegacyMethod { get; set; }

        public DateTime Date { get; set; }

        public string Status { get; set; } = AttendanceStatuses.Present;

        public string Method { get; set; } = AttendanceMethods.Manual;
    }
}
