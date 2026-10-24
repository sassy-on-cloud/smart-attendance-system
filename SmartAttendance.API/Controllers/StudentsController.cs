using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;
using SmartAttendance.API.DTOs;
using SmartAttendance.API.Models;

namespace SmartAttendance.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StudentsController(AppDbContext context) : ControllerBase
{
    [Authorize(Roles = "Admin,Teacher")]
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StudentResponseDto>>> GetStudents(CancellationToken cancellationToken)
    {
        return await context.Students.AsNoTracking()
            .Select(ResponseProjections.Student).ToListAsync(cancellationToken);
    }

    [Authorize(Roles = "Admin,Teacher")]
    [HttpGet("{id}")]
    public async Task<ActionResult<StudentResponseDto>> GetStudent(
        [Range(1, int.MaxValue)] int id, CancellationToken cancellationToken)
    {
        var student = await context.Students.AsNoTracking().Where(s => s.StudentId == id)
            .Select(ResponseProjections.Student).FirstOrDefaultAsync(cancellationToken);
        return student is null ? NotFound() : Ok(student);
    }

    [Authorize(Roles = "Admin")]
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
        await context.SaveChangesAsync(cancellationToken);

        var response = await context.Students.AsNoTracking().Where(s => s.StudentId == student.StudentId)
            .Select(ResponseProjections.Student).SingleAsync(cancellationToken);
        return CreatedAtAction(nameof(GetStudent), new { id = student.StudentId }, response);
    }
}
