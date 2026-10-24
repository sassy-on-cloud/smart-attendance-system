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
[Authorize(Policy = AuthPolicies.Staff)]
public class AttendanceSessionsController(AppDbContext context, AttendanceSessionService sessionService) : ControllerBase
{
    private int? ActorId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0 ? id : null;

    private IQueryable<AttendanceSession> AccessibleSessions()
    {
        var query = context.AttendanceSessions.AsNoTracking();
        var userId = ActorId;
        if (!userId.HasValue) return query.Where(_ => false);
        if (User.IsInRole("Admin")) return query.Where(_ => context.Users.Any(u => u.UserId == userId && u.Role == "Admin"));
        return query.Where(s => context.TeacherCourseAssignments.Any(a => a.CourseId == s.CourseId && a.TeacherUserId == userId && a.Teacher.Role == "Teacher"));
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AttendanceSessionResponseDto>>> GetSessions(
        [Range(1, int.MaxValue)] int? courseId, [RegularExpression("^(Open|Closed|Cancelled)$")] string? state,
        [Range(0, int.MaxValue)] int skip = 0, [Range(1, 100)] int take = 50, CancellationToken cancellationToken = default)
    {
        var query = AccessibleSessions();
        if (courseId.HasValue) query = query.Where(s => s.CourseId == courseId);
        if (state is not null) query = query.Where(s => s.State == state);
        return await query.OrderBy(s => s.StartsAtUtc).ThenBy(s => s.AttendanceSessionId).Skip(skip).Take(take)
            .Select(ResponseProjections.Session).ToListAsync(cancellationToken);
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
        CreateAttendanceSessionDto dto, CancellationToken cancellationToken) =>
        MapWriteResult(await sessionService.CreateAsync(dto, User.IsInRole("Admin"), ActorId, cancellationToken));

    [HttpPut("{id}")]
    public async Task<ActionResult<AttendanceSessionResponseDto>> UpdateSession(
        [Range(1, int.MaxValue)] int id, UpdateAttendanceSessionDto dto, CancellationToken cancellationToken) =>
        MapWriteResult(await sessionService.UpdateAsync(id, dto, User.IsInRole("Admin"), ActorId, cancellationToken));

    [HttpGet("{id}/history")]
    public async Task<ActionResult<IEnumerable<AttendanceSessionHistoryResponseDto>>> GetHistory(
        [Range(1, int.MaxValue)] int id, CancellationToken cancellationToken)
    {
        if (!await AccessibleSessions().AnyAsync(s => s.AttendanceSessionId == id, cancellationToken)) return NotFound();
        return await context.AttendanceSessionHistory.AsNoTracking().Where(h => h.AttendanceSessionId == id)
            .OrderBy(h => h.Version).Select(ResponseProjections.SessionHistory).ToListAsync(cancellationToken);
    }

    private ActionResult<AttendanceSessionResponseDto> MapWriteResult(SessionWriteResult result) => result.Outcome switch
    {
        SessionWriteOutcome.Created => CreatedAtAction(nameof(GetSession), new { id = result.Session!.AttendanceSessionId }, result.Session),
        SessionWriteOutcome.Updated => Ok(result.Session),
        SessionWriteOutcome.NotFound => NotFound(),
        SessionWriteOutcome.Forbidden => Forbid(),
        SessionWriteOutcome.Conflict => Conflict("The session changed, is cancelled, has attendance that locks its times, or no change was requested."),
        _ => BadRequest("A verified session is required.")
    };
}
