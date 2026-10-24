using SmartAttendance.API.Security;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;
using SmartAttendance.API.DTOs;
using SmartAttendance.API.Models;
using SmartAttendance.API.Services;

namespace SmartAttendance.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthPolicies.KnownRoles)]
public class AttendancesController(AppDbContext context, AttendanceService attendanceService) : ControllerBase
{
    private IQueryable<Attendance> AccessibleAttendances()
    {
        var query = context.Attendances.AsNoTracking();
        // Account identity is authoritative; StudentId is a separate database key.
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return query.Where(_ => false);

        if (User.IsInRole("Admin"))
            return query.Where(_ => context.Users.Any(u => u.UserId == userId && u.Role == "Admin"));

        if (User.IsInRole("Teacher"))
            return query.Where(a => context.TeacherCourseAssignments.Any(t => t.CourseId == a.CourseId && t.TeacherUserId == userId && t.Teacher.Role == "Teacher"));

        return query.Where(a => a.Student != null && a.Student.UserId == userId && a.Student.User != null && a.Student.User.Role == "Student");
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AttendanceResponseDto>>> GetAttendances(CancellationToken cancellationToken)
    {
        return await AccessibleAttendances().Select(ResponseProjections.Attendance).ToListAsync(cancellationToken);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AttendanceResponseDto>> GetAttendance(
        [Range(1, int.MaxValue)] int id, CancellationToken cancellationToken)
    {
        var attendance = await AccessibleAttendances().Where(a => a.AttendanceId == id)
            .Select(ResponseProjections.Attendance).FirstOrDefaultAsync(cancellationToken);
        // Foreign and missing records have the same response.
        return attendance is null ? NotFound() : Ok(attendance);
    }

    [Authorize(Policy = AuthPolicies.Staff)]
    [HttpPost]
    public async Task<ActionResult<AttendanceResponseDto>> CreateAttendance(
        CreateAttendanceDto dto, CancellationToken cancellationToken)
    {
        int? actorId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0 ? id : null;
        var result = await attendanceService.MarkManualAsync(dto, User.IsInRole("Admin"), actorId, cancellationToken);
        return MapWriteResult(result);
    }

    [Authorize(Policy = AuthPolicies.Staff)]
    [HttpPut("{id}")]
    public async Task<ActionResult<AttendanceResponseDto>> CorrectAttendance(
        [Range(1, int.MaxValue)] int id, CorrectAttendanceDto dto, CancellationToken cancellationToken)
    {
        int? actorId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) && userId > 0 ? userId : null;
        return MapWriteResult(await attendanceService.CorrectManualAsync(id, dto, User.IsInRole("Admin"), actorId, cancellationToken));
    }

    [Authorize(Policy = AuthPolicies.Staff)]
    [HttpGet("{id}/history")]
    public async Task<ActionResult<IEnumerable<AttendanceHistoryResponseDto>>> GetHistory(
        [Range(1, int.MaxValue)] int id, CancellationToken cancellationToken)
    {
        if (!await AccessibleAttendances().AnyAsync(a => a.AttendanceId == id, cancellationToken)) return NotFound();
        return await context.AttendanceHistory.AsNoTracking().Where(h => h.AttendanceId == id)
            .OrderBy(h => h.Version).Select(ResponseProjections.History).ToListAsync(cancellationToken);
    }

    private ActionResult<AttendanceResponseDto> MapWriteResult(AttendanceWriteResult result) => result.Outcome switch
    {
        AttendanceWriteOutcome.Created => CreatedAtAction(nameof(GetAttendance), new { id = result.Attendance!.AttendanceId }, result.Attendance),
        AttendanceWriteOutcome.Corrected => Ok(result.Attendance),
        AttendanceWriteOutcome.Forbidden => Forbid(),
        AttendanceWriteOutcome.MissingStudent => NotFound("Student does not exist."),
        AttendanceWriteOutcome.MissingSession or AttendanceWriteOutcome.NotFound => NotFound(),
        AttendanceWriteOutcome.Duplicate => Conflict("Attendance already exists for this student and session."),
        AttendanceWriteOutcome.Conflict => Conflict("The record changed, the session does not permit this operation, or no change was requested."),
        AttendanceWriteOutcome.NotEnrolled => BadRequest("Student must have a confirmed enrollment in the session's course."),
        AttendanceWriteOutcome.InvalidStudent => BadRequest("Student must be linked to a Student account."),
        _ => BadRequest("A verified attendance session is required.")
    };
}
