using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;
using SmartAttendance.API.Models;
using Microsoft.AspNetCore.Authorization;

namespace SmartAttendance.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]

    [Authorize]
    public class AttendancesController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AttendancesController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/attendances
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Attendance>>> GetAttendances()
        {
            return await _context.Attendances
                .Include(a => a.Student)
                .Include(a => a.Course)
                .ToListAsync();
        }

        // GET: api/attendances/1
        [HttpGet("{id}")]
        public async Task<ActionResult<Attendance>> GetAttendance(int id)
        {
            var attendance = await _context.Attendances
                .Include(a => a.Student)
                .Include(a => a.Course)
                .FirstOrDefaultAsync(a => a.AttendanceId == id);

            if (attendance == null)
            {
                return NotFound();
            }

            return attendance;
        }

        // POST: api/attendances
        [Authorize(Roles = "Admin,Teacher")]
        [HttpPost]
        public async Task<ActionResult<Attendance>> CreateAttendance(
            Attendance attendance)
        {
            var studentExists = await _context.Students
                .AnyAsync(s => s.StudentId == attendance.StudentId);

            if (!studentExists)
            {
                return BadRequest("Student does not exist.");
            }

            var courseExists = await _context.Courses
                .AnyAsync(c => c.CourseId == attendance.CourseId);

            if (!courseExists)
            {
                return BadRequest("Course does not exist.");
            }

            _context.Attendances.Add(attendance);
            await _context.SaveChangesAsync();

            return CreatedAtAction(
                nameof(GetAttendance),
                new { id = attendance.AttendanceId },
                attendance
            );
        }
    }
}