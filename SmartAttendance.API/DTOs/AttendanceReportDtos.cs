namespace SmartAttendance.API.DTOs;

public class ReportPageDto<T>
{
    public List<T> Items { get; set; } = [];
    public long TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public DateTime AsOfUtc { get; set; }
    public string PolicyVersion { get; set; } = "closed-past-v1";
}

public class AttendanceRecordReportDto
{
    public int AttendanceId { get; set; }
    public int StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string RollNumber { get; set; } = string.Empty;
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public int AttendanceSessionId { get; set; }
    public DateTime? StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public string SessionState { get; set; } = string.Empty;
    public bool IsLegacy { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; }
    public bool IsEligible { get; set; }
}

public class AttendanceStatisticsDto
{
    public long ClosedPastSessions { get; set; }
    public long OnTimePresentSessions { get; set; }
    public long LateSessions { get; set; }
    public long AbsentSessions { get; set; }
    public long ExcusedSessions { get; set; }
    public long UnmarkedSessions { get; set; }
    public long UnverifiedSessions { get; set; }
    public long PresentSessions => OnTimePresentSessions + LateSessions;
    public long TotalEligibleSessions => ClosedPastSessions - ExcusedSessions;
    public decimal? Percentage => TotalEligibleSessions == 0 ? null :
        Math.Round(100m * PresentSessions / TotalEligibleSessions, 2, MidpointRounding.AwayFromZero);
}

public class StudentCourseAttendanceDto : AttendanceStatisticsDto
{
    public int StudentId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string RollNumber { get; set; } = string.Empty;
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
}

public class OverallAttendanceDto : AttendanceStatisticsDto
{
    public DateTime AsOfUtc { get; set; }
    public string PolicyVersion { get; set; } = "closed-past-v1";
    public long ConfirmedStudentCoursePairs { get; set; }
}

public class TeacherCourseAttendanceDto : AttendanceStatisticsDto
{
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public long ConfirmedStudents { get; set; }
    public long CompletedMeetings { get; set; }
    public long IncompleteMeetings { get; set; }
    public long CancelledMeetings { get; set; }
    public long LegacyPlaceholders { get; set; }
}

public class AdminAttendanceDashboardDto
{
    public long GlobalUsers { get; set; }
    public long GlobalStudents { get; set; }
    public long GlobalTeachers { get; set; }
    public long GlobalCourses { get; set; }
    public long CompletedMeetings { get; set; }
    public long IncompleteMeetings { get; set; }
    public long CancelledMeetings { get; set; }
    public long LegacyPlaceholders { get; set; }
    public long RecordedAttendance { get; set; }
    public long LegacyAttendance { get; set; }
    public OverallAttendanceDto Attendance { get; set; } = new();
}

public class AttendanceReportPolicyDto
{
    public string Version { get; set; } = "closed-past-v1";
    public string DateFilterTimeZone { get; set; } = "UTC";
    public string Formula { get; set; } = "PresentSessions / TotalEligibleSessions * 100";
    public string PresentSessions { get; set; } = "Present + Late; current records only, never audit entries.";
    public string Eligibility { get; set; } = "Confirmed Student enrollments; verified Closed meetings ending on or before AsOfUtc.";
    public string Excused { get; set; } = "Excluded from that student's denominator and numerator.";
    public string Missing { get; set; } = "Eligible and Unmarked; contributes zero to PresentSessions.";
    public string Unverified { get; set; } = "Eligible but not Present when attached to an otherwise eligible verified meeting.";
    public string Exclusions { get; set; } = "Cancelled, legacy, Open, missing-time and future meetings; inferred enrollments.";
    public string EmptyDenominator { get; set; } = "Percentage is null, not zero or 100.";
    public string Overall { get; set; } = "Ratio of summed student-session counts, not an average of percentages.";
}
