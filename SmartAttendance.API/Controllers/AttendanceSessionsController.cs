using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;
using SmartAttendance.API.DTOs;
using SmartAttendance.API.Models;

namespace SmartAttendance.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,Teacher")]
public class AttendanceSessionsController(AppDbContext context) : ControllerBase
{
    private IQueryable<AttendanceSession> AccessibleSessions()
    {
        var query = context.AttendanceSessions.AsNoTracking();
        if (User.IsInRole("Admin")) return query;
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return query.Where(_ => false);
        return query.Where(s => context.TeacherCourseAssignments.Any(a => a.CourseId == s.CourseId && a.TeacherUserId == userId));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AttendanceSessionResponseDto>> GetSession(
        [Range(1, int.MaxValue)] int id, CancellationToken cancellationToken)
    {
        var result = await AccessibleSessions().Where(s => s.AttendanceSessionId == id)
            .Select(ResponseProjections.Session).FirstOrDefaultAsync(cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<AttendanceSessionResponseDto>> CreateSession(
        CreateAttendanceSessionDto dto, CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin"))
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
                !await context.TeacherCourseAssignments.AnyAsync(a => a.CourseId == dto.CourseId && a.TeacherUserId == userId, cancellationToken))
                return Forbid();
        }
        if (!await context.Courses.AnyAsync(c => c.CourseId == dto.CourseId, cancellationToken))
            return BadRequest("Course does not exist.");
        var session = new AttendanceSession
        {
            CourseId = dto.CourseId, StartsAtUtc = dto.StartsAt!.Value.UtcDateTime, EndsAtUtc = dto.EndsAt!.Value.UtcDateTime
        };
        context.AttendanceSessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);
        var result = await context.AttendanceSessions.Where(s => s.AttendanceSessionId == session.AttendanceSessionId)
            .Select(ResponseProjections.Session).SingleAsync(cancellationToken);
        return CreatedAtAction(nameof(GetSession), new { id = session.AttendanceSessionId }, result);
    }
}
