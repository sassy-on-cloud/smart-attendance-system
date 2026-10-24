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
[Authorize(Roles = "Admin,Teacher,Student")]
public class AttendancesController(AppDbContext context) : ControllerBase
{
    private IQueryable<Attendance> AccessibleAttendances()
    {
        var query = context.Attendances.AsNoTracking();
        if (User.IsInRole("Admin") || User.IsInRole("Teacher"))
            return query;

        // Account identity is authoritative; StudentId is a separate database key.
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return query.Where(_ => false);

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
        if (!await context.Students.AnyAsync(s => s.StudentId == dto.StudentId, cancellationToken))
            return BadRequest("Student does not exist.");
        if (!await context.Courses.AnyAsync(c => c.CourseId == dto.CourseId, cancellationToken))
            return BadRequest("Course does not exist.");

        var attendance = new Attendance
        {
            StudentId = dto.StudentId,
            CourseId = dto.CourseId,
            Date = dto.Date!.Value.UtcDateTime,
            Status = dto.Status,
            // Callers cannot assert recognition provenance on the manual endpoint.
            Method = "Manual"
        };
        context.Attendances.Add(attendance);
        await context.SaveChangesAsync(cancellationToken);
        var response = await context.Attendances.AsNoTracking().Where(a => a.AttendanceId == attendance.AttendanceId)
            .Select(ResponseProjections.Attendance).SingleAsync(cancellationToken);
        return CreatedAtAction(nameof(GetAttendance), new { id = attendance.AttendanceId }, response);
    }
}
