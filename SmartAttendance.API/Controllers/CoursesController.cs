using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;
using SmartAttendance.API.Models;
using Microsoft.AspNetCore.Authorization;


namespace SmartAttendance.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CoursesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CoursesController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/courses
        [Authorize(Roles = "Admin,Teacher,Student")]
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Course>>> GetCourses()
        {
            return await _context.Courses.ToListAsync();
        }

        // GET: api/courses/1
        [Authorize(Roles = "Admin,Teacher,Student")]
        [HttpGet("{id}")]
        public async Task<ActionResult<Course>> GetCourse(int id)
        {
            var course = await _context.Courses.FindAsync(id);

            if (course == null)
            {
                return NotFound();
            }

            return course;
        }

        // POST: api/courses
        [Authorize(Roles = "Admin,Teacher")]
        [HttpPost]
        public async Task<ActionResult<Course>> CreateCourse(Course course)
        {
            _context.Courses.Add(course);
            await _context.SaveChangesAsync();

            return CreatedAtAction(
                nameof(GetCourse),
                new { id = course.CourseId },
                course
            );
        }
    }
}