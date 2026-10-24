using SmartAttendance.API.Security;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SmartAttendance.API.DTOs;
using SmartAttendance.API.Reporting;

namespace SmartAttendance.API.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Policy = AuthPolicies.KnownRoles)]
[Tags("Attendance reports")]
[Produces("application/json")]
[ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public class AttendanceReportsController(AttendanceReportService reports) : ControllerBase, IAsyncActionFilter
{
    private AttendanceReportScope _scope = null!;
    [NonAction]
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var scope = await reports.ResolveScopeAsync(User, HttpContext.RequestAborted);
        if (scope is null) { context.Result = Forbid(); return; }
        _scope = scope;
        await next();
    }

    [HttpGet("policy")]
    [ProducesResponseType(typeof(AttendanceReportPolicyDto), StatusCodes.Status200OK)]
    [ReportEndpoint("Attendance calculation policy", "Returns the policy used by every statistical report. This endpoint contains no personal information.")]
    public ActionResult<AttendanceReportPolicyDto> GetPolicy() => new AttendanceReportPolicyDto();

    [HttpGet("attendance")]
    [ProducesResponseType(typeof(ReportPageDto<AttendanceRecordReportDto>), StatusCodes.Status200OK)]
    [ReportEndpoint("Filter attendance records by course, student, date or session", "Paged current attendance rows, including flagged legacy rows by default. A missing attendance row is not fabricated here: statistics expose UnmarkedSessions. Status/method filters apply only to this records view.")]
    public Task<ActionResult<ReportPageDto<AttendanceRecordReportDto>>> GetAttendance(
        [FromQuery] AttendanceRecordQuery query, CancellationToken cancellationToken) => Records(query, cancellationToken);

    [HttpGet("students/{studentId}/history")]
    [ProducesResponseType(typeof(ReportPageDto<AttendanceRecordReportDto>), StatusCodes.Status200OK)]
    [ReportEndpoint("Student attendance history", "Current records for one authorized Student profile. Historical corrections are reflected in the current status, not counted as additional sessions. Staff-only correction audit reasons are not exposed.")]
    public async Task<ActionResult<ReportPageDto<AttendanceRecordReportDto>>> GetStudentHistory(
        [Range(1, int.MaxValue)] int studentId, [FromQuery] AttendanceRecordQuery query, CancellationToken cancellationToken)
    {
        if (query.StudentId.HasValue && query.StudentId != studentId)
            return BadRequest("Route and query student IDs must agree.");
        query.StudentId = studentId;
        return await Records(query, cancellationToken);
    }

    [HttpGet("courses/{courseId}/records")]
    [ProducesResponseType(typeof(ReportPageDto<AttendanceRecordReportDto>), StatusCodes.Status200OK)]
    [ReportEndpoint("Course-wise attendance records", "Teacher must be assigned to the course. Student receives only their own records in that course, never a class roster.")]
    public async Task<ActionResult<ReportPageDto<AttendanceRecordReportDto>>> GetCourseRecords(
        [Range(1, int.MaxValue)] int courseId, [FromQuery] AttendanceRecordQuery query, CancellationToken cancellationToken)
    {
        if (query.CourseId.HasValue && query.CourseId != courseId)
            return BadRequest("Route and query course IDs must agree.");
        query.CourseId = courseId;
        return await Records(query, cancellationToken);
    }

    [HttpGet("sessions/{attendanceSessionId}/records")]
    [ProducesResponseType(typeof(ReportPageDto<AttendanceRecordReportDto>), StatusCodes.Status200OK)]
    [ReportEndpoint("Session-wise attendance records", "Filters by an actual meeting ID rather than a calendar date. Role and course scope apply to every row.")]
    public async Task<ActionResult<ReportPageDto<AttendanceRecordReportDto>>> GetSessionRecords(
        [Range(1, int.MaxValue)] int attendanceSessionId, [FromQuery] AttendanceRecordQuery query, CancellationToken cancellationToken)
    {
        if (query.AttendanceSessionId.HasValue && query.AttendanceSessionId != attendanceSessionId)
            return BadRequest("Route and query session IDs must agree.");
        query.AttendanceSessionId = attendanceSessionId;
        return await Records(query, cancellationToken);
    }

    [HttpGet("percentages")]
    [ProducesResponseType(typeof(ReportPageDto<StudentCourseAttendanceDto>), StatusCodes.Status200OK)]
    [ReportEndpoint("Attendance percentage per student and course", "One row per confirmed student/course enrollment, including zero eligible-session rows. Present and Late count as attended. Excused is removed only for the excused student; missing/unverified marks remain eligible but not attended. Percentage is rounded to two decimals after aggregation. Audit history is not counted.")]
    public async Task<ActionResult<ReportPageDto<StudentCourseAttendanceDto>>> GetPercentages(
        [FromQuery] AttendancePercentageQuery query, CancellationToken cancellationToken)
    {
        if (!await reports.CanAccessFiltersAsync(_scope, query, cancellationToken)) return NotFound();
        return await reports.PercentagesAsync(_scope, query, cancellationToken);
    }

    [HttpGet("overall")]
    [ProducesResponseType(typeof(OverallAttendanceDto), StatusCodes.Status200OK)]
    [ReportEndpoint("Overall attendance percentage", "Sum PresentSessions and TotalEligibleSessions across authorized filtered student/course enrollments, then divide. This is not an average of percentages. Status, method and paging do not narrow this denominator.")]
    public async Task<ActionResult<OverallAttendanceDto>> GetOverall(
        [FromQuery] AttendanceReportFilter query, CancellationToken cancellationToken)
    {
        if (!await reports.CanAccessFiltersAsync(_scope, query, cancellationToken)) return NotFound();
        return await reports.OverallAsync(_scope, query, cancellationToken);
    }

    [HttpGet("teachers/me/summary")]
    [ProducesResponseType(typeof(ReportPageDto<TeacherCourseAttendanceDto>), StatusCodes.Status200OK)]
    [Authorize(Policy = AuthPolicies.Teacher)]
    [ReportEndpoint("Current Teacher's attendance summaries", "Paged per-course summaries for courses assigned to the authenticated Teacher. Counts distinguish actual meetings from eligible student-session opportunities. Use courseId or percentage sorting.")]
    public Task<ActionResult<ReportPageDto<TeacherCourseAttendanceDto>>> GetMyTeacherSummary(
        [FromQuery] AttendancePercentageQuery query, CancellationToken cancellationToken) => TeacherSummary(_scope.UserId, query, cancellationToken);

    [HttpGet("teachers/{teacherUserId}/summary")]
    [ProducesResponseType(typeof(ReportPageDto<TeacherCourseAttendanceDto>), StatusCodes.Status200OK)]
    [Authorize(Policy = AuthPolicies.Staff)]
    [ReportEndpoint("Teacher attendance summaries", "Admin can inspect a Teacher's assigned-course summaries. Teacher can inspect only self; another Teacher ID returns 404. No marking-action audit counts are substituted for student attendance.")]
    public Task<ActionResult<ReportPageDto<TeacherCourseAttendanceDto>>> GetTeacherSummary(
        [Range(1, int.MaxValue)] int teacherUserId, [FromQuery] AttendancePercentageQuery query, CancellationToken cancellationToken) =>
        TeacherSummary(teacherUserId, query, cancellationToken);

    [HttpGet("admin/dashboard")]
    [ProducesResponseType(typeof(AdminAttendanceDashboardDto), StatusCodes.Status200OK)]
    [Authorize(Policy = AuthPolicies.Admin)]
    [ReportEndpoint("Admin attendance dashboard", "Global account/student/course totals plus filtered meeting/record counts and weighted attendance statistics. Global totals are all-time. Meeting counts respect course/session/date filters; studentId narrows attendance counts/statistics, not global or meeting totals.")]
    public async Task<ActionResult<AdminAttendanceDashboardDto>> GetDashboard(
        [FromQuery] AttendanceReportFilter query, CancellationToken cancellationToken)
    {
        if (_scope.Role != "Admin") return Forbid();
        if (!await reports.CanAccessFiltersAsync(_scope, query, cancellationToken)) return NotFound();
        return await reports.DashboardAsync(_scope, query, cancellationToken);
    }

    private async Task<ActionResult<ReportPageDto<AttendanceRecordReportDto>>> Records(AttendanceRecordQuery query, CancellationToken ct)
    {
        if (!await reports.CanAccessFiltersAsync(_scope, query, ct)) return NotFound();
        return await reports.RecordsAsync(_scope, query, ct);
    }
    private async Task<ActionResult<ReportPageDto<TeacherCourseAttendanceDto>>> TeacherSummary(int teacherUserId, AttendancePercentageQuery query, CancellationToken ct)
    {
        if (query.SortBy == "studentId") return BadRequest("Teacher course summaries sort by courseId or percentage.");
        if (!await reports.CanReadTeacherAsync(_scope, teacherUserId, ct) ||
            !await reports.CanAccessFiltersAsync(new(teacherUserId, "Teacher"), query, ct)) return NotFound();
        return await reports.TeacherSummaryAsync(teacherUserId, query, ct);
    }
}
