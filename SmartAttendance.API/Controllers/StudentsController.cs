using SmartAttendance.API.Security;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;
using SmartAttendance.API.DTOs;
using SmartAttendance.API.Models;
using System.Security.Claims;

namespace SmartAttendance.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StudentsController(AppDbContext context) : ControllerBase
{
    private IQueryable<Student> AccessibleStudents()
    {
        var query = context.Students.AsNoTracking();
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return query.Where(_ => false);
        var adminClaim = User.IsInRole(AppRoles.Admin);
        var teacherClaim = User.IsInRole(AppRoles.Teacher);
        return query.Where(student =>
            (adminClaim && context.Users.Any(u => u.UserId == userId && u.Role == AppRoles.Admin)) ||
            (teacherClaim && context.CourseEnrollments.Any(e => e.StudentId == student.StudentId &&
                context.TeacherCourseAssignments.Any(a => a.CourseId == e.CourseId && a.TeacherUserId == userId && a.Teacher.Role == AppRoles.Teacher))));
    }

    [Authorize(Policy = AuthPolicies.CurrentStaff)]
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StudentResponseDto>>> GetStudents(CancellationToken cancellationToken)
    {
        return await AccessibleStudents()
            .Select(ResponseProjections.Student).ToListAsync(cancellationToken);
    }

    [Authorize(Policy = AuthPolicies.CurrentStaff)]
    [HttpGet("{id}")]
    public async Task<ActionResult<StudentResponseDto>> GetStudent(
        [Range(1, int.MaxValue)] int id, CancellationToken cancellationToken)
    {
        var student = await AccessibleStudents().Where(s => s.StudentId == id)
            .Select(ResponseProjections.Student).FirstOrDefaultAsync(cancellationToken);
        return student is null ? NotFound() : Ok(student);
    }

    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpPost]
    public async Task<ActionResult<StudentResponseDto>> CreateStudent(
        CreateStudentDto dto, CancellationToken cancellationToken)
    {
        var user = await context.Users.AsNoTracking().Where(u => u.UserId == dto.UserId)
            .Select(ResponseProjections.User).FirstOrDefaultAsync(cancellationToken);
        if (user is null)
            return BadRequest("User does not exist.");
        if (user.Role != "Student")
            return BadRequest("A student profile must belong to a Student user.");
        if (await context.Students.AnyAsync(s => s.UserId == dto.UserId, cancellationToken))
            return Conflict("This user already has a student profile.");

        var student = new Student
        {
            UserId = dto.UserId,
            RollNumber = dto.RollNumber.Trim(),
            Department = dto.Department.Trim(),
            Semester = dto.Semester
        };
        context.Students.Add(student);
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            if (await context.Students.AsNoTracking().AnyAsync(s => s.UserId == dto.UserId, cancellationToken))
                return Conflict("This user already has a student profile.");
            throw;
        }

        var response = await context.Students.AsNoTracking().Where(s => s.StudentId == student.StudentId)
            .Select(ResponseProjections.Student).SingleAsync(cancellationToken);
        return CreatedAtAction(nameof(GetStudent), new { id = student.StudentId }, response);
    }
}
