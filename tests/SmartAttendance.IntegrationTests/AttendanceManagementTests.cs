using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartAttendance.API.Data;
using SmartAttendance.API.Models;
using Xunit;

namespace SmartAttendance.IntegrationTests;

public sealed class AttendanceManagementTests : IClassFixture<SecurityApiFactory>, IDisposable
{
    private readonly SecurityApiFactory _factory;
    private readonly HttpClient _client;
    public AttendanceManagementTests(SecurityApiFactory factory)
    {
        _factory = factory; factory.ResetDatabase();
        _client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Authenticate("Admin", "11");
    }
    private void Authenticate(string role, string? userId) =>
        _client.DefaultRequestHeaders.Authorization = new("Bearer", _factory.Token(role, userId));

    private Task<HttpResponseMessage> Mark(int studentId = 1, int sessionId = 2) =>
        _client.PostAsJsonAsync("/api/attendances", new { studentId, attendanceSessionId = sessionId, status = "Present" });
    private Task<HttpResponseMessage> Correct(int id, string status = "Absent", int version = 1, string reason = "Register reviewed.") =>
        _client.PutAsJsonAsync($"/api/attendances/{id}", new { status, expectedVersion = version, reason });
    private Task<HttpResponseMessage> UpdateSession(int id, string state, int version = 1, string startsAt = "2026-10-20T05:00:00Z", string endsAt = "2026-10-20T06:00:00Z") =>
        _client.PutAsJsonAsync($"/api/attendancesessions/{id}", new { state, expectedVersion = version, startsAt, endsAt, reason = "Session register reviewed." });

    [Theory]
    [InlineData("Admin", "11")]
    [InlineData("Teacher", "22")]
    public async Task Authorized_mark_and_correction_record_actor_method_and_atomic_history(string role, string userId)
    {
        Authenticate(role, userId);
        var mark = await Mark(); Assert.Equal(HttpStatusCode.Created, mark.StatusCode);
        var original = await mark.Content.ReadFromJsonAsync<JsonElement>();
        var id = original.GetProperty("attendanceId").GetInt32();
        Assert.Equal(int.Parse(userId), original.GetProperty("recordedByUserId").GetInt32());
        Assert.Equal("Manual", original.GetProperty("method").GetString());
        Assert.Equal(1, original.GetProperty("version").GetInt32());
        var correction = await Correct(id); Assert.Equal(HttpStatusCode.OK, correction.StatusCode);
        var corrected = await correction.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(int.Parse(userId), corrected.GetProperty("lastModifiedByUserId").GetInt32());
        Assert.Equal("Absent", corrected.GetProperty("status").GetString());
        Assert.Equal("Manual", corrected.GetProperty("method").GetString());
        Assert.Equal(2, corrected.GetProperty("version").GetInt32());
        Assert.Equal(original.GetProperty("date").GetString(), corrected.GetProperty("date").GetString());
        var history = await _client.GetFromJsonAsync<JsonElement[]>($"/api/attendances/{id}/history");
        Assert.Equal(2, history!.Length);
        Assert.Equal("Created", history[0].GetProperty("changeType").GetString());
        Assert.Equal("Corrected", history[1].GetProperty("changeType").GetString());
        Assert.Equal("Present", history[1].GetProperty("previousStatus").GetString());
        Assert.Equal("Manual", history[1].GetProperty("previousMethod").GetString());
        Assert.All(history, entry => Assert.Equal(int.Parse(userId), entry.GetProperty("actorUserId").GetInt32()));
        Assert.DoesNotContain("passwordHash", JsonSerializer.Serialize(history), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Correction_preserves_original_actor_and_rejects_stale_or_no_op_updates()
    {
        var mark = await Mark(); var id = (await mark.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("attendanceId").GetInt32();
        Authenticate("Teacher", "22");
        Assert.Equal(HttpStatusCode.OK, (await Correct(id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Correct(id, "Late", version: 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Correct(id, "Absent", version: 2)).StatusCode);
        var current = await _client.GetFromJsonAsync<JsonElement>($"/api/attendances/{id}");
        Assert.Equal(11, current.GetProperty("recordedByUserId").GetInt32());
        Assert.Equal(22, current.GetProperty("lastModifiedByUserId").GetInt32());
        Assert.Equal(2, (await _client.GetFromJsonAsync<JsonElement[]>($"/api/attendances/{id}/history"))!.Length);
    }

    [Theory]
    [InlineData(99999, 2, 404)]
    [InlineData(1, 99999, 404)]
    [InlineData(1, 1, 409)]
    public async Task Missing_students_sessions_and_duplicates_have_expected_status(int studentId, int sessionId, int status) =>
        Assert.Equal((HttpStatusCode)status, (await Mark(studentId, sessionId)).StatusCode);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unenrolled_or_unconfirmed_students_cannot_be_marked(bool inferred)
    {
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var enrollment = await db.CourseEnrollments.FindAsync(1, 1);
        if (inferred) enrollment!.IsLegacyInferred = true;
        // Seed attendance must be retained: use a different course/session for the nonenrolled scenario.
        if (!inferred)
        {
            db.Courses.Add(new Course { CourseId = 2, CourseCode = "C2" });
            db.AttendanceSessions.Add(new AttendanceSession { AttendanceSessionId = 3, CourseId = 2,
                StartsAtUtc = DateTime.UtcNow, EndsAtUtc = DateTime.UtcNow.AddHours(1) });
        }
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Mark(sessionId: inferred ? 2 : 3)).StatusCode);
        Assert.Empty(await db.AttendanceHistory.ToListAsync());
    }

    [Theory]
    [InlineData("Anonymous", null, 401)]
    [InlineData("Student", "101", 403)]
    [InlineData("Unknown", "11", 403)]
    [InlineData("Teacher", "999", 403)]
    [InlineData("Admin", null, 403)]
    [InlineData("Admin", "999", 403)]
    [InlineData("Admin", "101", 403)]
    public async Task Unauthorized_mark_correction_and_session_updates_are_rejected(string role, string? userId, int status)
    {
        _client.DefaultRequestHeaders.Authorization = null;
        if (role != "Anonymous") Authenticate(role, userId);
        Assert.Equal((HttpStatusCode)status, (await Mark()).StatusCode);
        Assert.Equal((HttpStatusCode)status, (await Correct(1)).StatusCode);
        Assert.Equal((HttpStatusCode)status, (await UpdateSession(2, "Closed")).StatusCode);
    }

    [Fact]
    public async Task Unassigned_teacher_cannot_correct_or_read_history_and_student_sees_only_own_records()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new User { UserId = 23, Role = "Teacher" }); await db.SaveChangesAsync();
        }
        Authenticate("Teacher", "23");
        Assert.Equal(HttpStatusCode.Forbidden, (await Correct(1)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/attendances/1/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/attendancesessions/1/history")).StatusCode);
        Authenticate("Student", "101");
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/attendances/1/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/attendances/2")).StatusCode);
    }

    [Theory]
    [InlineData("{\"status\":\"Absent\",\"expectedVersion\":1,\"reason\":\" \"}")]
    [InlineData("{\"status\":\"Invented\",\"expectedVersion\":1,\"reason\":\"Reviewed\"}")]
    [InlineData("{\"status\":\"Absent\",\"expectedVersion\":0,\"reason\":\"Reviewed\"}")]
    [InlineData("{\"status\":\"Absent\",\"expectedVersion\":1,\"reason\":\"Reviewed\",\"method\":\"FaceRecognition\"}")]
    [InlineData("{\"status\":\"Absent\",\"expectedVersion\":1,\"reason\":\"Reviewed\",\"studentId\":2}")]
    [InlineData("{\"status\":\"Absent\",\"expectedVersion\":1,\"reason\":\"Reviewed\",\"actorUserId\":11}")]
    public async Task Invalid_corrections_and_overposting_cannot_modify_attendance(string body)
    {
        var response = await _client.PutAsync("/api/attendances/1", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("Present", (await db.Attendances.FindAsync(1))!.Status);
        Assert.Empty(await db.AttendanceHistory.ToListAsync());
    }

    [Fact]
    public async Task Client_recognition_claims_and_actor_fields_are_rejected()
    {
        var before = await Mark(); Assert.Equal(HttpStatusCode.Created, before.StatusCode);
        var response = await _client.PostAsJsonAsync("/api/attendances", new
        {
            studentId = 2, attendanceSessionId = 2, status = "Present", method = "FaceRecognition", actorUserId = 22, confidence = 1.0
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.AttendanceHistory.CountAsync());
    }

    [Fact]
    public async Task Session_lifecycle_audit_and_closed_session_corrections_work()
    {
        var created = await _client.PostAsJsonAsync("/api/attendancesessions", new { courseId = 1,
            startsAt = "2026-10-20T09:00:00Z", endsAt = "2026-10-20T10:00:00Z" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var session = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = session.GetProperty("attendanceSessionId").GetInt32();
        Assert.Equal(11, session.GetProperty("createdByUserId").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await UpdateSession(id, "Closed", startsAt: "2026-10-20T09:00:00Z", endsAt: "2026-10-20T10:00:00Z")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Mark(sessionId: id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UpdateSession(id, "Open", version: 2, startsAt: "2026-10-20T09:00:00Z", endsAt: "2026-10-20T10:00:00Z")).StatusCode);
        var mark = await Mark(sessionId: id); Assert.Equal(HttpStatusCode.Created, mark.StatusCode);
        var attendanceId = (await mark.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("attendanceId").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await UpdateSession(id, "Closed", version: 3, startsAt: "2026-10-20T09:00:00Z", endsAt: "2026-10-20T10:00:00Z")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Correct(attendanceId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UpdateSession(id, "Cancelled", version: 4, startsAt: "2026-10-20T09:00:00Z", endsAt: "2026-10-20T10:00:00Z")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Correct(attendanceId, "Late", version: 2)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await UpdateSession(id, "Open", version: 5, startsAt: "2026-10-20T09:00:00Z", endsAt: "2026-10-20T10:00:00Z")).StatusCode);
        var history = await _client.GetFromJsonAsync<JsonElement[]>($"/api/attendancesessions/{id}/history");
        Assert.Equal(5, history!.Length);
        Assert.All(history, h => Assert.Equal(11, h.GetProperty("actorUserId").GetInt32()));
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/attendances/{attendanceId}")).StatusCode);
    }

    [Fact]
    public async Task Times_lock_after_first_mark_and_stale_session_edits_return_conflict()
    {
        Assert.Equal(HttpStatusCode.OK, (await UpdateSession(2, "Open", startsAt: "2026-10-20T07:00:00Z", endsAt: "2026-10-20T08:00:00Z")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await UpdateSession(2, "Closed", version: 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Mark()).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await UpdateSession(2, "Closed", version: 2)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UpdateSession(2, "Closed", version: 2, startsAt: "2026-10-20T07:00:00Z", endsAt: "2026-10-20T08:00:00Z")).StatusCode);
    }

    [Fact]
    public async Task Session_listing_is_filtered_paged_and_scoped()
    {
        Assert.Equal(2, (await _client.GetFromJsonAsync<JsonElement[]>("/api/attendancesessions?courseId=1&state=Open"))!.Length);
        Assert.Single((await _client.GetFromJsonAsync<JsonElement[]>("/api/attendancesessions?take=1&skip=1"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/attendancesessions?take=101")).StatusCode);
        Authenticate("Teacher", "999");
        Assert.Empty((await _client.GetFromJsonAsync<JsonElement[]>("/api/attendancesessions"))!);
        Authenticate("Student", "101");
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/attendancesessions")).StatusCode);
    }

    [Fact]
    public async Task Missing_corrections_sessions_and_courses_return_not_found()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Correct(9999)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UpdateSession(9999, "Closed")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync("/api/attendancesessions", new { courseId = 9999,
            startsAt = "2026-10-20T09:00:00Z", endsAt = "2026-10-20T10:00:00Z" })).StatusCode);
    }

    [Fact]
    public async Task Manual_correction_preserves_previous_recognition_label_without_trusting_it()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Attendances.FindAsync(1))!.Method = "FaceRecognition";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await Correct(1, "Present")).StatusCode);
        var history = await _client.GetFromJsonAsync<JsonElement[]>("/api/attendances/1/history");
        var change = Assert.Single(history!);
        Assert.Equal("FaceRecognition", change.GetProperty("previousMethod").GetString());
        Assert.Equal("Manual", change.GetProperty("method").GetString());
        Assert.Equal(11, change.GetProperty("actorUserId").GetInt32());
    }

    [Fact]
    public async Task Corrections_require_a_student_account_and_confirmed_enrollment()
    {
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var enrollment = await db.CourseEnrollments.FindAsync(1, 1);
        enrollment!.IsLegacyInferred = true; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Correct(1, "Absent")).StatusCode);
        enrollment.IsLegacyInferred = false;
        (await db.Users.FindAsync(101))!.Role = "Teacher"; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await Correct(1, "Absent")).StatusCode);
        Assert.Empty(await db.AttendanceHistory.ToListAsync());
    }

    [Fact]
    public async Task Legacy_sessions_cannot_be_modified_or_corrected_into_verified_attendance()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AttendanceSessions.Add(new AttendanceSession { AttendanceSessionId = 99, CourseId = 1, IsLegacy = true, LegacyAttendanceId = 99 });
            db.Attendances.Add(new Attendance { AttendanceId = 99, AttendanceSessionId = 99, CourseId = 1, StudentId = 1 });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Correct(99, "Absent")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UpdateSession(99, "Closed")).StatusCode);
    }

    public void Dispose() => _client.Dispose();
}
