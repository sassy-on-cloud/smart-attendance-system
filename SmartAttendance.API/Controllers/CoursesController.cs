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
public class CoursesController(AppDbContext context) : ControllerBase
{
    [Authorize(Roles = "Admin,Teacher,Student")]
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CourseResponseDto>>> GetCourses(CancellationToken cancellationToken)
    {
        return await context.Courses.AsNoTracking()
            .Select(ResponseProjections.Course).ToListAsync(cancellationToken);
    }

    [Authorize(Roles = "Admin,Teacher,Student")]
    [HttpGet("{id}")]
    public async Task<ActionResult<CourseResponseDto>> GetCourse(
        [Range(1, int.MaxValue)] int id, CancellationToken cancellationToken)
    {
        var course = await context.Courses.AsNoTracking().Where(c => c.CourseId == id)
            .Select(ResponseProjections.Course).FirstOrDefaultAsync(cancellationToken);
        return course is null ? NotFound() : Ok(course);
    }

    [Authorize(Roles = "Admin,Teacher")]
    [HttpPost]
    public async Task<ActionResult<CourseResponseDto>> CreateCourse(
        CreateCourseDto dto, CancellationToken cancellationToken)
    {
        var course = new Course { CourseName = dto.CourseName.Trim(), CourseCode = dto.CourseCode.Trim() };
        context.Courses.Add(course);
        await context.SaveChangesAsync(cancellationToken);
        var response = await context.Courses.AsNoTracking().Where(c => c.CourseId == course.CourseId)
            .Select(ResponseProjections.Course).SingleAsync(cancellationToken);
        return CreatedAtAction(nameof(GetCourse), new { id = course.CourseId }, response);
    }
}
