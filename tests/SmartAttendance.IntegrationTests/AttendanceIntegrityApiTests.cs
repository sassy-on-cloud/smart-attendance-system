using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartAttendance.API.Data;
using SmartAttendance.API.Models;
using Xunit;

namespace SmartAttendance.IntegrationTests;

public sealed class AttendanceIntegrityApiTests : IClassFixture<SecurityApiFactory>, IDisposable
{
    private readonly SecurityApiFactory _factory;
    private readonly HttpClient _client;
    public AttendanceIntegrityApiTests(SecurityApiFactory factory)
    {
        _factory = factory; factory.ResetDatabase();
        _client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Authenticate("Admin", "11");
    }

    private void Authenticate(string role, string id) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.Token(role, id));

    [Fact]
    public async Task Duplicate_session_mark_returns_conflict_but_second_same_day_session_is_allowed()
    {
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync("/api/attendances", new { studentId = 1, attendanceSessionId = 1, status = "Present" })).StatusCode);
        var response = await _client.PostAsJsonAsync("/api/attendances", new { studentId = 1, attendanceSessionId = 2, status = "Excused" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, json.GetProperty("courseId").GetInt32());
        Assert.Equal(2, json.GetProperty("attendanceSessionId").GetInt32());
        Assert.False(json.GetProperty("isLegacy").GetBoolean());
        Assert.Equal("Manual", json.GetProperty("method").GetString());
    }

    [Fact]
    public async Task Unassigned_teacher_cannot_read_or_mark_attendance_or_schedule_sessions()
    {
        Authenticate("Teacher", "999");
        Assert.Empty(await _client.GetFromJsonAsync<JsonElement[]>("/api/attendances") ?? []);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/attendances/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/attendancesessions/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.PostAsJsonAsync("/api/attendances", new { studentId = 1, attendanceSessionId = 2, status = "Present" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.PostAsJsonAsync("/api/attendancesessions", new { courseId = 1, startsAt = "2026-10-20T04:00:00Z", endsAt = "2026-10-20T05:00:00Z" })).StatusCode);
    }

    [Fact]
    public async Task Admin_can_assign_teacher_then_teacher_can_schedule_and_mark_only_that_course()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Courses.Add(new Course { CourseId = 2, CourseCode = "C2" }); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/courses/2/teacher-assignments", new { teacherUserId = 22 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync("/api/courses/2/teacher-assignments", new { teacherUserId = 22 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/courses/2/teacher-assignments", new { teacherUserId = 101 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/courses/2/enrollments", new { studentId = 1 })).StatusCode);
        Authenticate("Teacher", "22");
        var session = await _client.PostAsJsonAsync("/api/attendancesessions", new { courseId = 2, startsAt = "2026-10-20T09:45:00+05:45", endsAt = "2026-10-20T10:45:00+05:45" });
        Assert.Equal(HttpStatusCode.Created, session.StatusCode);
        var json = await session.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new DateTime(2026, 10, 20, 4, 0, 0), json.GetProperty("startsAtUtc").GetDateTime());
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(session.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/attendances", new { studentId = 1, attendanceSessionId = json.GetProperty("attendanceSessionId").GetInt32(), status = "Present" })).StatusCode);
    }

    [Fact]
    public async Task Inferred_enrollment_requires_explicit_admin_confirmation()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var enrollment = await db.CourseEnrollments.FindAsync(1, 1);
            enrollment!.IsLegacyInferred = true; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/attendances", new { studentId = 1, attendanceSessionId = 2, status = "Present" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync("/api/courses/1/enrollments", new { studentId = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/attendances", new { studentId = 1, attendanceSessionId = 2, status = "Present" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync("/api/courses/1/enrollments", new { studentId = 1 })).StatusCode);
    }

    [Fact]
    public async Task Legacy_placeholders_cannot_be_used_for_new_marks()
    {
        int sessionId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var legacy = new AttendanceSession { CourseId = 1, IsLegacy = true, LegacyAttendanceId = 999 };
            db.AttendanceSessions.Add(legacy); await db.SaveChangesAsync(); sessionId = legacy.AttendanceSessionId;
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/attendances", new { studentId = 1, attendanceSessionId = sessionId, status = "Present" })).StatusCode);
    }

    public static IEnumerable<object[]> ManagementAuthorization()
    {
        foreach (var role in new[] { "Anonymous", "Student", "Teacher", "Unknown" })
        foreach (var route in new[] { "/api/courses/1/enrollments", "/api/courses/1/teacher-assignments" })
            yield return [role, route, role == "Anonymous" ? 401 : 403];
    }

    [Theory]
    [MemberData(nameof(ManagementAuthorization))]
    public async Task Enrollment_and_assignment_management_is_admin_only(string role, string route, int status)
    {
        _client.DefaultRequestHeaders.Authorization = null;
        if (role != "Anonymous") Authenticate(role, "22");
        Assert.Equal((HttpStatusCode)status, (await _client.PostAsJsonAsync(route, new { })).StatusCode);
    }

    [Theory]
    [InlineData("Anonymous", 401)]
    [InlineData("Student", 403)]
    [InlineData("Unknown", 403)]
    public async Task Session_routes_preserve_staff_only_access(string role, int status)
    {
        _client.DefaultRequestHeaders.Authorization = null;
        if (role != "Anonymous") Authenticate(role, "101");
        Assert.Equal((HttpStatusCode)status, (await _client.GetAsync("/api/attendancesessions/1")).StatusCode);
        Assert.Equal((HttpStatusCode)status, (await _client.PostAsJsonAsync("/api/attendancesessions", new { })).StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"courseId":0,"startsAt":"2026-10-20T04:00:00Z","endsAt":"2026-10-20T05:00:00Z"}""")]
    [InlineData("""{"courseId":1,"startsAt":"2026-10-20T04:00:00Z","endsAt":"2026-10-20T04:00:00Z"}""")]
    [InlineData("""{"courseId":1,"startsAt":"bad","endsAt":"2026-10-20T05:00:00Z"}""")]
    [InlineData("""{"courseId":1,"startsAt":"2026-10-20T04:00:00Z","endsAt":"2026-10-20T05:00:00Z","isLegacy":true}""")]
    [InlineData("""{"courseId":1,"startsAt":"2026-10-20T04:00:00Z","endsAt":"2026-10-20T05:00:00Z","legacyAttendanceId":1}""")]
    public async Task Invalid_session_requests_and_provenance_overposting_are_rejected(string json)
    {
        var response = await _client.PostAsync("/api/attendancesessions", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<AppDbContext>().AttendanceSessions.CountAsync());
    }

    public void Dispose() => _client.Dispose();
}
