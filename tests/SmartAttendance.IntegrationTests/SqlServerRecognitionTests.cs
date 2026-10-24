using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Models;
using Xunit;

namespace SmartAttendance.IntegrationTests;

[Collection("SqlServer")]
public sealed class SqlServerRecognitionTests(SqlServerFixture fixture)
{
    [SqlServerFact]
    public async Task Mock_contract_checks_real_sql_relationships_and_duplicates_without_writing_attendance()
    {
        await using var db = fixture.NewDatabase();
        await db.Database.MigrateAsync(); // Only the fixture's private disposable database.
        var teacher = new User { Name = "Synthetic Teacher", Email = "teacher@example.test", Role = "Teacher" };
        var user = new User { Name = "Synthetic Student", Email = "student@example.test", Role = "Student" };
        var student = new Student { User = user, RollNumber = "SYN-01", Department = "Test", Semester = 1 };
        var course = new Course { CourseCode = "SYN101", CourseName = "Test" };
        var session = new AttendanceSession { Course = course, StartsAtUtc = DateTime.UtcNow.AddMinutes(-5), EndsAtUtc = DateTime.UtcNow.AddMinutes(55) };
        db.Users.Add(teacher); db.Students.Add(student); db.AttendanceSessions.Add(session); await db.SaveChangesAsync();
        db.CourseEnrollments.Add(new CourseEnrollment { StudentId = student.StudentId, CourseId = course.CourseId });
        db.TeacherCourseAssignments.Add(new TeacherCourseAssignment { TeacherUserId = teacher.UserId, CourseId = course.CourseId });
        await db.SaveChangesAsync();
        var mock = new MockRecognitionComponent
        {
            Recognize = (c, _) => Task.FromResult(MockRecognitionComponent.Match(c, student.StudentId))
        };
        using var host = new SecurityApiFactory { SqlServerTestOptions = new DbContextOptionsBuilder<SmartAttendance.API.Data.AppDbContext>()
            .UseSqlServer(db.Database.GetConnectionString()).Options, RecognitionComponent = mock };
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token("Teacher", teacher.UserId.ToString()));
        var route = $"/api/recognition/sessions/{session.AttendanceSessionId}/recognize";
        var response = await client.PostAsJsonAsync(route, new { captureId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("attendanceRecorded").GetBoolean());
        Assert.Empty(await db.Attendances.ToListAsync()); Assert.Empty(await db.AttendanceHistory.ToListAsync());
        // Existing attendance remains the authoritative duplicate guard, never overwritten by a match.
        db.Attendances.Add(new Attendance { StudentId = student.StudentId, CourseId = course.CourseId,
            AttendanceSessionId = session.AttendanceSessionId, Status = "Absent", Method = "Manual" });
        await db.SaveChangesAsync();
        response = await client.PostAsJsonAsync(route, new { captureId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("DuplicateAttendance", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("outcome").GetString());
        Assert.Equal("Absent", (await db.Attendances.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await db.AttendanceHistory.ToListAsync());
    }
}
