using SmartAttendance.API.Security;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;
using SmartAttendance.API.DTOs;
using SmartAttendance.API.Models;
using SmartAttendance.API.Services;
using System.Security.Claims;

namespace SmartAttendance.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CoursesController(AppDbContext context, AttendanceAccess access) : ControllerBase
{
    [Authorize(Policy = AuthPolicies.KnownRoles)]
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CourseResponseDto>>> GetCourses(CancellationToken cancellationToken)
    {
        return await context.Courses.AsNoTracking()
            .Select(ResponseProjections.Course).ToListAsync(cancellationToken);
    }

    [Authorize(Policy = AuthPolicies.KnownRoles)]
    [HttpGet("{id}")]
    public async Task<ActionResult<CourseResponseDto>> GetCourse(
        [Range(1, int.MaxValue)] int id, CancellationToken cancellationToken)
    {
        var course = await context.Courses.AsNoTracking().Where(c => c.CourseId == id)
            .Select(ResponseProjections.Course).FirstOrDefaultAsync(cancellationToken);
        return course is null ? NotFound() : Ok(course);
    }

    [Authorize(Policy = AuthPolicies.Staff)]
    [HttpPost]
    public async Task<ActionResult<CourseResponseDto>> CreateCourse(
        CreateCourseDto dto, CancellationToken cancellationToken)
    {
        int? actorId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) && userId > 0 ? userId : null;
        if (!await access.IsStaffActorAsync(User.IsInRole(AppRoles.Admin), actorId, cancellationToken)) return Forbid();
        var course = new Course { CourseName = dto.CourseName.Trim(), CourseCode = dto.CourseCode.Trim() };
        context.Courses.Add(course);
        await context.SaveChangesAsync(cancellationToken);
        var response = await context.Courses.AsNoTracking().Where(c => c.CourseId == course.CourseId)
            .Select(ResponseProjections.Course).SingleAsync(cancellationToken);
        return CreatedAtAction(nameof(GetCourse), new { id = course.CourseId }, response);
    }

    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpPost("{id}/enrollments")]
    public async Task<ActionResult<EnrollmentResponseDto>> EnrollStudent(
        [Range(1, int.MaxValue)] int id, EnrollStudentDto dto, CancellationToken cancellationToken)
    {
        if (!await context.Courses.AnyAsync(c => c.CourseId == id, cancellationToken) ||
            !await context.Students.AnyAsync(s => s.StudentId == dto.StudentId && s.User != null && s.User.Role == "Student", cancellationToken))
            return BadRequest("An existing course and Student account are required.");
        var enrollment = await context.CourseEnrollments.FindAsync([dto.StudentId, id], cancellationToken);
        if (enrollment is not null)
        {
            if (!enrollment.IsLegacyInferred) return Conflict("Student is already enrolled.");
            enrollment.IsLegacyInferred = false; // Explicit Admin confirmation of historical evidence.
        }
        else
        {
            enrollment = new CourseEnrollment { StudentId = dto.StudentId, CourseId = id };
            context.CourseEnrollments.Add(enrollment);
        }
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            if (await context.CourseEnrollments.AsNoTracking().AnyAsync(e => e.StudentId == dto.StudentId && e.CourseId == id, cancellationToken))
                return Conflict("Student is already enrolled.");
            throw;
        }
        return Ok(new EnrollmentResponseDto { StudentId = dto.StudentId, CourseId = id, IsLegacyInferred = false });
    }

    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpPost("{id}/teacher-assignments")]
    public async Task<ActionResult<TeacherAssignmentResponseDto>> AssignTeacher(
        [Range(1, int.MaxValue)] int id, AssignTeacherDto dto, CancellationToken cancellationToken)
    {
        if (!await context.Courses.AnyAsync(c => c.CourseId == id, cancellationToken) ||
            !await context.Users.AnyAsync(u => u.UserId == dto.TeacherUserId && u.Role == "Teacher", cancellationToken))
            return BadRequest("An existing course and Teacher account are required.");
        if (await context.TeacherCourseAssignments.AnyAsync(a => a.CourseId == id && a.TeacherUserId == dto.TeacherUserId, cancellationToken))
            return Conflict("Teacher is already assigned.");
        context.TeacherCourseAssignments.Add(new TeacherCourseAssignment { CourseId = id, TeacherUserId = dto.TeacherUserId });
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            if (await context.TeacherCourseAssignments.AsNoTracking().AnyAsync(a => a.CourseId == id && a.TeacherUserId == dto.TeacherUserId, cancellationToken))
                return Conflict("Teacher is already assigned.");
            throw;
        }
        return Ok(new TeacherAssignmentResponseDto { TeacherUserId = dto.TeacherUserId, CourseId = id });
    }
}
