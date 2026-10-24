using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;
using SmartAttendance.API.DTOs;
using SmartAttendance.API.Models;

namespace SmartAttendance.API.Reporting;

public record AttendanceReportScope(int UserId, string Role);

public class AttendanceReportService(AppDbContext context, TimeProvider clock)
{
    public DateTime AsOfUtc { get; } = clock.GetUtcNow().UtcDateTime;

    public async Task<AttendanceReportScope?> ResolveScopeAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0) return null;
        var role = await context.Users.AsNoTracking().Where(u => u.UserId == userId).Select(u => u.Role).SingleOrDefaultAsync(ct);
        return role is "Admin" or "Teacher" or "Student" && user.IsInRole(role) ? new(userId, role) : null;
    }

    private IQueryable<CourseEnrollment> ScopedEnrollments(AttendanceReportScope scope)
    {
        var query = context.CourseEnrollments.AsNoTracking();
        if (scope.Role == "Student") return query.Where(e => e.Student.UserId == scope.UserId);
        if (scope.Role == "Teacher") return query.Where(e => context.TeacherCourseAssignments.Any(a => a.CourseId == e.CourseId && a.TeacherUserId == scope.UserId));
        return query;
    }
    private IQueryable<Course> ScopedCourses(AttendanceReportScope scope)
    {
        var query = context.Courses.AsNoTracking();
        if (scope.Role == "Teacher") return query.Where(c => context.TeacherCourseAssignments.Any(a => a.CourseId == c.CourseId && a.TeacherUserId == scope.UserId));
        if (scope.Role == "Student") return query.Where(c => context.CourseEnrollments.Any(e => e.CourseId == c.CourseId && e.Student.UserId == scope.UserId));
        return query;
    }
    private IQueryable<Attendance> ScopedRecords(AttendanceReportScope scope)
    {
        var query = context.Attendances.AsNoTracking();
        if (scope.Role == "Student") return query.Where(a => a.Student != null && a.Student.UserId == scope.UserId);
        if (scope.Role == "Teacher") return query.Where(a => context.TeacherCourseAssignments.Any(t => t.CourseId == a.CourseId && t.TeacherUserId == scope.UserId));
        return query;
    }
    public async Task<bool> CanAccessFiltersAsync(AttendanceReportScope scope, AttendanceReportFilter q, CancellationToken ct)
    {
        if (q.StudentId.HasValue)
        {
            var students = context.Students.AsNoTracking().Where(s => s.StudentId == q.StudentId);
            if (scope.Role == "Student") students = students.Where(s => s.UserId == scope.UserId);
            if (scope.Role == "Teacher") students = students.Where(s => context.CourseEnrollments.Any(e => e.StudentId == s.StudentId &&
                context.TeacherCourseAssignments.Any(a => a.CourseId == e.CourseId && a.TeacherUserId == scope.UserId)));
            if (!await students.AnyAsync(ct)) return false;
        }
        if (q.CourseId.HasValue && !await ScopedCourses(scope).AnyAsync(c => c.CourseId == q.CourseId, ct)) return false;
        var accessibleCourses = ScopedCourses(scope);
        if (q.AttendanceSessionId.HasValue && !await context.AttendanceSessions.AnyAsync(s => s.AttendanceSessionId == q.AttendanceSessionId &&
            accessibleCourses.Any(c => c.CourseId == s.CourseId), ct)) return false;
        return true;
    }

    // Calendar filtering uses meeting start in UTC. Unknown legacy dates never fall back to marking time.
    private IQueryable<AttendanceSession> Sessions(AttendanceReportScope scope, AttendanceReportFilter q)
    {
        var courses = ScopedCourses(scope);
        var query = context.AttendanceSessions.AsNoTracking().Where(s => courses.Any(c => c.CourseId == s.CourseId));
        if (q.CourseId.HasValue) query = query.Where(s => s.CourseId == q.CourseId);
        if (q.AttendanceSessionId.HasValue) query = query.Where(s => s.AttendanceSessionId == q.AttendanceSessionId);
        if (q.FromDate.HasValue)
        {
            var start = q.FromDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(s => s.StartsAtUtc >= start);
        }
        if (q.ToDate.HasValue)
        {
            var end = q.ToDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(s => s.StartsAtUtc < end);
        }
        return query;
    }
    private IQueryable<AttendanceSession> CompletedSessions(AttendanceReportScope scope, AttendanceReportFilter q) =>
        Sessions(scope, q).Where(s => !s.IsLegacy && s.State == AttendanceSessionStates.Closed &&
            s.StartsAtUtc != null && s.EndsAtUtc != null && s.EndsAtUtc <= AsOfUtc);

    public async Task<ReportPageDto<AttendanceRecordReportDto>> RecordsAsync(AttendanceReportScope scope, AttendanceRecordQuery q, CancellationToken ct)
    {
        var sessions = Sessions(scope, q);
        var records = ScopedRecords(scope).Where(a => sessions.Any(s => s.AttendanceSessionId == a.AttendanceSessionId));
        if (q.StudentId.HasValue) records = records.Where(a => a.StudentId == q.StudentId);
        if (!q.IncludeLegacy) records = records.Where(a => !a.Session.IsLegacy);
        if (q.Status is not null) records = records.Where(a => a.Status == q.Status);
        if (q.Method is not null) records = records.Where(a => a.Method == q.Method);
        var total = await records.LongCountAsync(ct);
        var desc = q.Direction == "desc";
        IOrderedQueryable<Attendance> ordered = q.SortBy switch
        {
            "studentId" => desc ? records.OrderByDescending(a => a.StudentId) : records.OrderBy(a => a.StudentId),
            "courseId" => desc ? records.OrderByDescending(a => a.CourseId) : records.OrderBy(a => a.CourseId),
            "status" => desc ? records.OrderByDescending(a => a.Status) : records.OrderBy(a => a.Status),
            "recordedAt" => desc ? records.OrderByDescending(a => a.Date) : records.OrderBy(a => a.Date),
            _ => desc ? records.OrderByDescending(a => a.Session.StartsAtUtc) : records.OrderBy(a => a.Session.StartsAtUtc)
        };
        var skip = (long)(q.Page - 1) * q.PageSize;
        var items = skip > int.MaxValue ? [] : await ordered.ThenBy(a => a.AttendanceId).Skip((int)skip).Take(q.PageSize)
            .Select(a => new AttendanceRecordReportDto
            {
                AttendanceId = a.AttendanceId, StudentId = a.StudentId, StudentName = a.Student!.User!.Name, RollNumber = a.Student.RollNumber,
                CourseId = a.CourseId, CourseCode = a.Course!.CourseCode, CourseName = a.Course.CourseName,
                AttendanceSessionId = a.AttendanceSessionId, StartsAtUtc = a.Session.StartsAtUtc, EndsAtUtc = a.Session.EndsAtUtc,
                SessionState = a.Session.State, IsLegacy = a.Session.IsLegacy, Status = a.Status, Method = a.Method, RecordedAt = a.Date,
                IsEligible = !a.Session.IsLegacy && a.Session.State == "Closed" && a.Session.StartsAtUtc != null &&
                    a.Session.EndsAtUtc != null && a.Session.EndsAtUtc <= AsOfUtc && !a.Enrollment.IsLegacyInferred &&
                    a.Student.User!.Role == "Student" && a.Status != "Excused"
            }).ToListAsync(ct);
        return new() { Items = items, TotalCount = total, Page = q.Page, PageSize = q.PageSize, AsOfUtc = AsOfUtc };
    }

    private IQueryable<EnrollmentCounts> StudentCourseCounts(AttendanceReportScope scope, AttendanceReportFilter q)
    {
        var enrollments = ScopedEnrollments(scope).Where(e => !e.IsLegacyInferred && e.Student.User != null && e.Student.User.Role == "Student");
        if (q.StudentId.HasValue) enrollments = enrollments.Where(e => e.StudentId == q.StudentId);
        if (q.CourseId.HasValue) enrollments = enrollments.Where(e => e.CourseId == q.CourseId);
        if (q.AttendanceSessionId.HasValue) enrollments = enrollments.Where(e => context.AttendanceSessions.Any(s =>
            s.AttendanceSessionId == q.AttendanceSessionId && s.CourseId == e.CourseId));
        var pairs = from e in enrollments
                    join s in CompletedSessions(scope, q) on e.CourseId equals s.CourseId
                    join a in context.Attendances.AsNoTracking() on new { e.StudentId, s.AttendanceSessionId }
                        equals new { a.StudentId, a.AttendanceSessionId } into marks
                    from a in marks.DefaultIfEmpty()
                    select new { e.StudentId, e.CourseId, Status = a == null ? null : a.Status };
        var counts = pairs.GroupBy(p => new { p.StudentId, p.CourseId }).Select(g => new SessionCounts
        {
            StudentId = g.Key.StudentId, CourseId = g.Key.CourseId,
            ClosedPastSessions = g.LongCount(), OnTimePresentSessions = g.LongCount(p => p.Status == "Present"),
            LateSessions = g.LongCount(p => p.Status == "Late"), AbsentSessions = g.LongCount(p => p.Status == "Absent"),
            ExcusedSessions = g.LongCount(p => p.Status == "Excused"), UnmarkedSessions = g.LongCount(p => p.Status == null),
            UnverifiedSessions = g.LongCount(p => p.Status == "Unverified")
        });
        return from e in enrollments
               join c in counts on new { e.StudentId, e.CourseId } equals new { c.StudentId, c.CourseId } into groups
               from c in groups.DefaultIfEmpty()
               select new EnrollmentCounts
               {
                   StudentId = e.StudentId, CourseId = e.CourseId, StudentName = e.Student.User!.Name, RollNumber = e.Student.RollNumber,
                   CourseCode = e.Course.CourseCode, CourseName = e.Course.CourseName,
                   ClosedPastSessions = (long?)c.ClosedPastSessions ?? 0, OnTimePresentSessions = (long?)c.OnTimePresentSessions ?? 0,
                   LateSessions = (long?)c.LateSessions ?? 0, AbsentSessions = (long?)c.AbsentSessions ?? 0,
                   ExcusedSessions = (long?)c.ExcusedSessions ?? 0, UnmarkedSessions = (long?)c.UnmarkedSessions ?? 0,
                   UnverifiedSessions = (long?)c.UnverifiedSessions ?? 0,
                   SortPercentage = (c.ClosedPastSessions - c.ExcusedSessions) == 0 ? null :
                       100.0 * (c.OnTimePresentSessions + c.LateSessions) / (c.ClosedPastSessions - c.ExcusedSessions)
               };
    }

    public async Task<ReportPageDto<StudentCourseAttendanceDto>> PercentagesAsync(AttendanceReportScope scope, AttendancePercentageQuery q, CancellationToken ct)
    {
        var rows = StudentCourseCounts(scope, q);
        var total = await rows.LongCountAsync(ct);
        var desc = q.Direction == "desc";
        var ordered = q.SortBy switch
        {
            "studentId" => desc ? rows.OrderByDescending(r => r.StudentId) : rows.OrderBy(r => r.StudentId),
            "percentage" => desc ? rows.OrderByDescending(r => r.SortPercentage) : rows.OrderBy(r => r.SortPercentage),
            _ => desc ? rows.OrderByDescending(r => r.CourseId) : rows.OrderBy(r => r.CourseId)
        };
        var skip = (long)(q.Page - 1) * q.PageSize;
        var data = skip > int.MaxValue ? [] : await ordered.ThenBy(r => r.CourseId).ThenBy(r => r.StudentId)
            .Skip((int)skip).Take(q.PageSize).ToListAsync(ct);
        return new() { TotalCount = total, Items = data.Select(ToStudentDto).ToList(), Page = q.Page, PageSize = q.PageSize, AsOfUtc = AsOfUtc };
    }

    public async Task<OverallAttendanceDto> OverallAsync(AttendanceReportScope scope, AttendanceReportFilter q, CancellationToken ct)
    {
        var result = await StudentCourseCounts(scope, q).GroupBy(_ => 1).Select(g => new OverallAttendanceDto
        {
            ConfirmedStudentCoursePairs = g.LongCount(), ClosedPastSessions = g.Sum(r => r.ClosedPastSessions),
            OnTimePresentSessions = g.Sum(r => r.OnTimePresentSessions), LateSessions = g.Sum(r => r.LateSessions),
            AbsentSessions = g.Sum(r => r.AbsentSessions), ExcusedSessions = g.Sum(r => r.ExcusedSessions),
            UnmarkedSessions = g.Sum(r => r.UnmarkedSessions), UnverifiedSessions = g.Sum(r => r.UnverifiedSessions)
        }).SingleOrDefaultAsync(ct) ?? new();
        result.AsOfUtc = AsOfUtc;
        return result;
    }

    public async Task<bool> CanReadTeacherAsync(AttendanceReportScope scope, int teacherUserId, CancellationToken ct) =>
        (scope.Role == "Admin" || scope.Role == "Teacher" && scope.UserId == teacherUserId) &&
        await context.Users.AnyAsync(u => u.UserId == teacherUserId && u.Role == "Teacher", ct);

    public async Task<ReportPageDto<TeacherCourseAttendanceDto>> TeacherSummaryAsync(int teacherUserId, AttendancePercentageQuery q, CancellationToken ct)
    {
        var scope = new AttendanceReportScope(teacherUserId, "Teacher");
        var courses = ScopedCourses(scope);
        if (q.CourseId.HasValue) courses = courses.Where(c => c.CourseId == q.CourseId);
        if (q.AttendanceSessionId.HasValue) courses = courses.Where(c => context.AttendanceSessions.Any(s => s.CourseId == c.CourseId && s.AttendanceSessionId == q.AttendanceSessionId));
        var sums = StudentCourseCounts(scope, q).GroupBy(r => r.CourseId).Select(g => new CourseCounts
        {
            CourseId = g.Key, ConfirmedStudents = g.LongCount(), ClosedPastSessions = g.Sum(r => r.ClosedPastSessions),
            OnTimePresentSessions = g.Sum(r => r.OnTimePresentSessions), LateSessions = g.Sum(r => r.LateSessions),
            AbsentSessions = g.Sum(r => r.AbsentSessions), ExcusedSessions = g.Sum(r => r.ExcusedSessions),
            UnmarkedSessions = g.Sum(r => r.UnmarkedSessions), UnverifiedSessions = g.Sum(r => r.UnverifiedSessions)
        });
        var meetings = Sessions(scope, q);
        var query = from course in courses
                    join count in sums on course.CourseId equals count.CourseId into grouped
                    from c in grouped.DefaultIfEmpty()
                    select new TeacherCourseAttendanceDto
                    {
                        CourseId = course.CourseId, CourseCode = course.CourseCode, CourseName = course.CourseName,
                        ConfirmedStudents = (long?)c.ConfirmedStudents ?? 0,
                        ClosedPastSessions = (long?)c.ClosedPastSessions ?? 0, OnTimePresentSessions = (long?)c.OnTimePresentSessions ?? 0,
                        LateSessions = (long?)c.LateSessions ?? 0, AbsentSessions = (long?)c.AbsentSessions ?? 0,
                        ExcusedSessions = (long?)c.ExcusedSessions ?? 0, UnmarkedSessions = (long?)c.UnmarkedSessions ?? 0,
                        UnverifiedSessions = (long?)c.UnverifiedSessions ?? 0,
                        CompletedMeetings = meetings.LongCount(s => s.CourseId == course.CourseId && !s.IsLegacy && s.State == "Closed" &&
                            s.StartsAtUtc != null && s.EndsAtUtc != null && s.EndsAtUtc <= AsOfUtc),
                        CancelledMeetings = meetings.LongCount(s => s.CourseId == course.CourseId && !s.IsLegacy && s.State == "Cancelled"),
                        IncompleteMeetings = meetings.LongCount(s => s.CourseId == course.CourseId && !s.IsLegacy && s.State != "Cancelled" &&
                            (s.State != "Closed" || s.StartsAtUtc == null || s.EndsAtUtc == null || s.EndsAtUtc > AsOfUtc)),
                        LegacyPlaceholders = meetings.LongCount(s => s.CourseId == course.CourseId && s.IsLegacy)
                    };
        var total = await query.LongCountAsync(ct);
        var desc = q.Direction == "desc";
        var ordered = q.SortBy == "percentage" ? (desc
            ? query.OrderByDescending(r => r.ClosedPastSessions == r.ExcusedSessions ? (double?)null : 100.0 * (r.OnTimePresentSessions + r.LateSessions) / (r.ClosedPastSessions - r.ExcusedSessions))
            : query.OrderBy(r => r.ClosedPastSessions == r.ExcusedSessions ? (double?)null : 100.0 * (r.OnTimePresentSessions + r.LateSessions) / (r.ClosedPastSessions - r.ExcusedSessions)))
            : (desc ? query.OrderByDescending(r => r.CourseId) : query.OrderBy(r => r.CourseId));
        var skip = (long)(q.Page - 1) * q.PageSize;
        var items = skip > int.MaxValue ? [] : await ordered.ThenBy(r => r.CourseId).Skip((int)skip).Take(q.PageSize).ToListAsync(ct);
        return new() { Items = items, TotalCount = total, Page = q.Page, PageSize = q.PageSize, AsOfUtc = AsOfUtc };
    }

    public async Task<AdminAttendanceDashboardDto> DashboardAsync(AttendanceReportScope scope, AttendanceReportFilter q, CancellationToken ct)
    {
        var sessions = Sessions(scope, q);
        var sessionCounts = await sessions.GroupBy(_ => 1).Select(g => new
        {
            Completed = g.LongCount(s => !s.IsLegacy && s.State == "Closed" && s.StartsAtUtc != null && s.EndsAtUtc != null && s.EndsAtUtc <= AsOfUtc),
            Cancelled = g.LongCount(s => !s.IsLegacy && s.State == "Cancelled"), Legacy = g.LongCount(s => s.IsLegacy),
            Incomplete = g.LongCount(s => !s.IsLegacy && s.State != "Cancelled" && (s.State != "Closed" || s.StartsAtUtc == null || s.EndsAtUtc == null || s.EndsAtUtc > AsOfUtc))
        }).SingleOrDefaultAsync(ct);
        var records = ScopedRecords(scope).Where(a => sessions.Any(s => s.AttendanceSessionId == a.AttendanceSessionId));
        if (q.StudentId.HasValue) records = records.Where(a => a.StudentId == q.StudentId);
        var recordCounts = await records.GroupBy(_ => 1).Select(g => new { Total = g.LongCount(), Legacy = g.LongCount(a => a.Session.IsLegacy) }).SingleOrDefaultAsync(ct);
        return new()
        {
            GlobalUsers = await context.Users.LongCountAsync(ct), GlobalStudents = await context.Students.LongCountAsync(ct),
            GlobalTeachers = await context.Users.LongCountAsync(u => u.Role == "Teacher", ct), GlobalCourses = await context.Courses.LongCountAsync(ct),
            CompletedMeetings = sessionCounts?.Completed ?? 0, IncompleteMeetings = sessionCounts?.Incomplete ?? 0,
            CancelledMeetings = sessionCounts?.Cancelled ?? 0, LegacyPlaceholders = sessionCounts?.Legacy ?? 0,
            RecordedAttendance = recordCounts?.Total ?? 0, LegacyAttendance = recordCounts?.Legacy ?? 0,
            Attendance = await OverallAsync(scope, q, ct)
        };
    }

    private static StudentCourseAttendanceDto ToStudentDto(EnrollmentCounts r) => new()
    {
        StudentId = r.StudentId, StudentName = r.StudentName, RollNumber = r.RollNumber, CourseId = r.CourseId,
        CourseCode = r.CourseCode, CourseName = r.CourseName, ClosedPastSessions = r.ClosedPastSessions,
        OnTimePresentSessions = r.OnTimePresentSessions, LateSessions = r.LateSessions, AbsentSessions = r.AbsentSessions,
        ExcusedSessions = r.ExcusedSessions, UnmarkedSessions = r.UnmarkedSessions, UnverifiedSessions = r.UnverifiedSessions
    };

    private class SessionCounts
    {
        public int StudentId { get; set; }
        public int CourseId { get; set; }
        public long ClosedPastSessions { get; set; }
        public long OnTimePresentSessions { get; set; }
        public long LateSessions { get; set; }
        public long AbsentSessions { get; set; }
        public long ExcusedSessions { get; set; }
        public long UnmarkedSessions { get; set; }
        public long UnverifiedSessions { get; set; }
    }
    private sealed class EnrollmentCounts : SessionCounts
    {
        public string StudentName { get; set; } = string.Empty;
        public string RollNumber { get; set; } = string.Empty;
        public string CourseCode { get; set; } = string.Empty;
        public string CourseName { get; set; } = string.Empty;
        public double? SortPercentage { get; set; }
    }
    private sealed class CourseCounts : SessionCounts { public long ConfirmedStudents { get; set; } }
}
