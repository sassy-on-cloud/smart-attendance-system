using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;
using SmartAttendance.API.Models;

namespace SmartAttendance.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StudentsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public StudentsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/students
        [Authorize(Roles = "Admin,Teacher")]
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Student>>> GetStudents()
        {
            return await _context.Students
                .Include(s => s.User)
                .ToListAsync();
        }

        // GET: api/students/1
        [Authorize(Roles = "Admin,Teacher")]
        [HttpGet("{id}")]
        public async Task<ActionResult<Student>> GetStudent(int id)
        {
            var student = await _context.Students
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.StudentId == id);

            if (student == null)
            {
                return NotFound();
            }

            return student;
        }

        // POST: api/students
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<ActionResult<Student>> CreateStudent(Student student)
        {
            _context.Students.Add(student);
            await _context.SaveChangesAsync();

            return CreatedAtAction(
                nameof(GetStudent),
                new { id = student.StudentId },
                student
            );
        }
    }
}