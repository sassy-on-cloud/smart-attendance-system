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
[Authorize(Roles = "Admin,Teacher,Student")]
public class AttendancesController(AppDbContext context, AttendanceService attendanceService) : ControllerBase
{
    private IQueryable<Attendance> AccessibleAttendances()
    {
        var query = context.Attendances.AsNoTracking();
        if (User.IsInRole("Admin"))
            return query;

        // Account identity is authoritative; StudentId is a separate database key.
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return query.Where(_ => false);

        if (User.IsInRole("Teacher"))
            return query.Where(a => context.TeacherCourseAssignments.Any(t => t.CourseId == a.CourseId && t.TeacherUserId == userId));

        return query.Where(a => a.Student != null && a.Student.UserId == userId);
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

    [Authorize(Roles = "Admin,Teacher")]
    [HttpPost]
    public async Task<ActionResult<AttendanceResponseDto>> CreateAttendance(
        CreateAttendanceDto dto, CancellationToken cancellationToken)
    {
        int? actorId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0 ? id : null;
        var result = await attendanceService.MarkManualAsync(dto, User.IsInRole("Admin"), actorId, cancellationToken);
        return result.Outcome switch
        {
            AttendanceWriteOutcome.Created => CreatedAtAction(nameof(GetAttendance),
                new { id = result.Attendance!.AttendanceId }, result.Attendance),
            AttendanceWriteOutcome.Forbidden => Forbid(),
            AttendanceWriteOutcome.Duplicate => Conflict("Attendance already exists for this student and session."),
            AttendanceWriteOutcome.NotEnrolled => BadRequest("Student must have a confirmed enrollment in the session's course."),
            _ => BadRequest("A verified attendance session is required.")
        };
    }
}
