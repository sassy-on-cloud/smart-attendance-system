using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartAttendance.API.Data;
using Xunit;

namespace SmartAttendance.IntegrationTests;

public sealed class AttendanceReportingTests : IDisposable
{
    private readonly SecurityApiFactory _factory = new() { TestClock = new FixedReportClock() };
    private readonly HttpClient _client;
    private readonly ReportingIds _ids;
    public AttendanceReportingTests()
    {
        _factory.ResetEmptyDatabase();
        using var scope = _factory.Services.CreateScope();
        _ids = ReportingTestData.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>()).GetAwaiter().GetResult();
        _client = _factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Authenticate("Admin", _ids.Admin);
    }
    private void Authenticate(string role, int id) => _client.DefaultRequestHeaders.Authorization = new("Bearer", _factory.Token(role, id.ToString()));
    private async Task<JsonElement> Get(string uri)
    {
        var response = await _client.GetAsync(uri);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{uri}: {response.StatusCode}: {text}");
        Assert.DoesNotContain(_factory.SigningKey, text);
        Assert.DoesNotContain("passwordHash", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connectionString", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("old@example.test", text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
    private static JsonElement Single(JsonElement page) => Assert.Single(page.GetProperty("items").EnumerateArray());

    [Fact]
    public async Task Student_course_percentage_counts_late_excused_missing_unverified_and_exclusions_explicitly()
    {
        Authenticate("Student", _ids.StudentUser);
        var report = Single(await Get($"/api/reports/percentages?courseId={_ids.Course}"));
        Assert.Equal(6, report.GetProperty("closedPastSessions").GetInt64());
        Assert.Equal(2, report.GetProperty("presentSessions").GetInt64());
        Assert.Equal(1, report.GetProperty("onTimePresentSessions").GetInt64());
        Assert.Equal(1, report.GetProperty("lateSessions").GetInt64());
        Assert.Equal(1, report.GetProperty("excusedSessions").GetInt64());
        Assert.Equal(1, report.GetProperty("unmarkedSessions").GetInt64());
        Assert.Equal(1, report.GetProperty("unverifiedSessions").GetInt64());
        Assert.Equal(5, report.GetProperty("totalEligibleSessions").GetInt64());
        Assert.Equal(40m, report.GetProperty("percentage").GetDecimal());
    }

    [Fact]
    public async Task Overall_is_weighted_not_an_average_of_student_course_percentages()
    {
        Authenticate("Teacher", _ids.Teacher);
        var overall = await Get("/api/reports/overall");
        Assert.Equal(4, overall.GetProperty("presentSessions").GetInt64());
        Assert.Equal(11, overall.GetProperty("totalEligibleSessions").GetInt64());
        Assert.Equal(36.36m, overall.GetProperty("percentage").GetDecimal());
        Authenticate("Student", _ids.StudentUser);
        Assert.Equal(50m, (await Get("/api/reports/overall")).GetProperty("percentage").GetDecimal());
        Authenticate("Admin", _ids.Admin);
        Assert.Equal(41.67m, (await Get("/api/reports/overall")).GetProperty("percentage").GetDecimal());
    }

    [Fact]
    public async Task Empty_courses_and_all_excused_return_null_percentage()
    {
        var empty = Single(await Get($"/api/reports/percentages?studentId={_ids.Student}&courseId={_ids.EmptyCourse}"));
        Assert.Equal(0, empty.GetProperty("totalEligibleSessions").GetInt64()); Assert.Equal(JsonValueKind.Null, empty.GetProperty("percentage").ValueKind);
        var excused = await Get($"/api/reports/overall?studentId={_ids.Student}&attendanceSessionId={_ids.ExcusedSession}");
        Assert.Equal(1, excused.GetProperty("excusedSessions").GetInt64()); Assert.Equal(JsonValueKind.Null, excused.GetProperty("percentage").ValueKind);
    }

    [Fact]
    public async Task Partially_marked_closed_session_counts_missing_student_but_excuses_only_the_excused_student()
    {
        Authenticate("Teacher", _ids.Teacher);
        var overall = await Get($"/api/reports/overall?attendanceSessionId={_ids.ExcusedSession}");
        Assert.Equal(2, overall.GetProperty("closedPastSessions").GetInt64());
        Assert.Equal(1, overall.GetProperty("excusedSessions").GetInt64());
        Assert.Equal(1, overall.GetProperty("unmarkedSessions").GetInt64());
        Assert.Equal(1, overall.GetProperty("totalEligibleSessions").GetInt64());
        Assert.Equal(0m, overall.GetProperty("percentage").GetDecimal());
        Authenticate("Student", _ids.OtherStudentUser);
        Assert.Equal(20m, (await Get($"/api/reports/overall?courseId={_ids.Course}")).GetProperty("percentage").GetDecimal());
    }

    [Fact]
    public async Task Date_filters_are_half_open_utc_calendar_ranges_even_for_a_meeting_crossing_midnight()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var meeting = new SmartAttendance.API.Models.AttendanceSession
            {
                CourseId = _ids.Course, StartsAtUtc = new DateTime(2026, 10, 20, 23, 30, 0, DateTimeKind.Utc),
                EndsAtUtc = new DateTime(2026, 10, 21, 0, 30, 0, DateTimeKind.Utc), State = "Closed"
            };
            db.AttendanceSessions.Add(meeting); await db.SaveChangesAsync();
            db.Attendances.Add(new SmartAttendance.API.Models.Attendance
            {
                StudentId = _ids.Student, CourseId = _ids.Course, AttendanceSessionId = meeting.AttendanceSessionId,
                Status = "Present", Method = "Manual", Date = ReportingTestData.Now.UtcDateTime
            });
            await db.SaveChangesAsync();
        }
        Authenticate("Student", _ids.StudentUser);
        var day = await Get($"/api/reports/overall?courseId={_ids.Course}&fromDate=2026-10-20&toDate=2026-10-20");
        Assert.Equal(3, day.GetProperty("totalEligibleSessions").GetInt64());
        Assert.Equal(100m, day.GetProperty("percentage").GetDecimal());
        var next = await Get($"/api/reports/attendance?courseId={_ids.Course}&fromDate=2026-10-21&toDate=2026-10-21");
        Assert.Equal(1, next.GetProperty("totalCount").GetInt64());
        Assert.Equal("Excused", Single(next).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Inferred_enrollment_history_is_visible_to_its_owner_but_never_creates_eligibility()
    {
        Authenticate("Student", _ids.InferredStudentUser);
        Assert.Empty((await Get("/api/reports/percentages")).GetProperty("items").EnumerateArray());
        var overall = await Get("/api/reports/overall"); Assert.Equal(0, overall.GetProperty("totalEligibleSessions").GetInt64());
        Assert.Equal(JsonValueKind.Null, overall.GetProperty("percentage").ValueKind);
        Assert.False(Single(await Get($"/api/reports/students/{_ids.InferredStudent}/history")).GetProperty("isEligible").GetBoolean());
    }

    [Fact]
    public async Task Legacy_rows_remain_flagged_history_and_never_inflate_percentages()
    {
        Authenticate("Student", _ids.StudentUser);
        var history = await Get($"/api/reports/students/{_ids.Student}/history");
        var legacy = history.GetProperty("items").EnumerateArray().Where(r => r.GetProperty("isLegacy").GetBoolean()).ToArray();
        Assert.Equal(2, legacy.Length); Assert.All(legacy, row => Assert.False(row.GetProperty("isEligible").GetBoolean()));
        var modern = await Get($"/api/reports/students/{_ids.Student}/history?includeLegacy=false");
        Assert.Equal(history.GetProperty("totalCount").GetInt64() - 2, modern.GetProperty("totalCount").GetInt64());
        var onlyLegacy = await Get($"/api/reports/overall?attendanceSessionId={_ids.LegacySession}");
        Assert.Equal(0, onlyLegacy.GetProperty("totalEligibleSessions").GetInt64());
    }

    [Fact]
    public async Task Date_and_session_filters_use_meeting_time_not_marking_time_and_preserve_same_day_meetings()
    {
        Authenticate("Student", _ids.StudentUser);
        var date = await Get($"/api/reports/attendance?courseId={_ids.Course}&fromDate=2026-10-20&toDate=2026-10-20&sortBy=sessionDate&direction=asc");
        Assert.Equal(4, date.GetProperty("totalCount").GetInt64()); // Two Closed + Open + Cancelled records.
        Assert.All(date.GetProperty("items").EnumerateArray(), r => { Assert.False(r.GetProperty("isLegacy").GetBoolean()); Assert.StartsWith("2026-10-20", r.GetProperty("startsAtUtc").GetString()); });
        var first = Single(await Get($"/api/reports/sessions/{_ids.FirstSession}/records"));
        Assert.Equal("Present", first.GetProperty("status").GetString());
        var second = Single(await Get($"/api/reports/sessions/{_ids.SecondSession}/records"));
        Assert.Equal("Late", second.GetProperty("status").GetString());
        var percent = await Get($"/api/reports/overall?courseId={_ids.Course}&fromDate=2026-10-20&toDate=2026-10-22");
        Assert.Equal(66.67m, percent.GetProperty("percentage").GetDecimal());
    }

    [Fact]
    public async Task Pagination_sorting_and_record_filters_are_stable_and_do_not_change_statistics()
    {
        var filter = $"/api/reports/attendance?studentId={_ids.Student}&courseId={_ids.Course}&status=Present&method=Manual&includeLegacy=false&sortBy=recordedAt&direction=asc&pageSize=2";
        var first = await Get(filter + "&page=1"); var second = await Get(filter + "&page=2");
        Assert.Equal(4, first.GetProperty("totalCount").GetInt64());
        var ids = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).Select(r => r.GetProperty("attendanceId").GetInt32()).ToArray();
        Assert.Equal(4, ids.Distinct().Count()); Assert.Equal(ids.Order(), ids);
        var sorted = await Get("/api/reports/percentages?sortBy=percentage&direction=desc&pageSize=100");
        var values = sorted.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("percentage").ValueKind == JsonValueKind.Null ? (decimal?)null : r.GetProperty("percentage").GetDecimal()).ToArray();
        Assert.Equal(values.OrderByDescending(v => v), values);
        Assert.Empty((await Get("/api/reports/attendance?page=2147483647&pageSize=100")).GetProperty("items").EnumerateArray());
        Assert.Equal(41.67m, (await Get("/api/reports/overall?status=Present")).GetProperty("percentage").GetDecimal());
    }

    [Fact]
    public async Task Student_cannot_expand_scope_using_routes_or_filters()
    {
        Authenticate("Student", _ids.StudentUser);
        foreach (var route in new[] { $"students/{_ids.OtherStudent}/history", $"attendance?studentId={_ids.OtherStudent}",
            $"percentages?studentId={_ids.OtherStudent}", $"overall?studentId={_ids.OtherStudent}", $"courses/{_ids.HiddenCourse}/records" })
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/reports/" + route)).StatusCode);
        foreach (var route in new[] { "attendance", $"courses/{_ids.Course}/records", $"sessions/{_ids.FirstSession}/records" })
            Assert.All((await Get("/api/reports/" + route)).GetProperty("items").EnumerateArray(), row => Assert.Equal(_ids.Student, row.GetProperty("studentId").GetInt32()));
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/reports/admin/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/reports/teachers/me/summary")).StatusCode);
    }

    [Fact]
    public async Task Teacher_reports_only_assigned_courses_and_own_teacher_summary()
    {
        Authenticate("Teacher", _ids.Teacher);
        foreach (var route in new[] { $"courses/{_ids.HiddenCourse}/records", $"students/{_ids.HiddenStudent}/history",
            $"overall?courseId={_ids.HiddenCourse}", $"teachers/{_ids.OtherTeacher}/summary" })
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/reports/" + route)).StatusCode);
        var summary = await Get("/api/reports/teachers/me/summary?sortBy=percentage&direction=desc");
        Assert.Equal(3, summary.GetProperty("totalCount").GetInt64());
        var course = summary.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("courseId").GetInt32() == _ids.Course);
        Assert.Equal(30m, course.GetProperty("percentage").GetDecimal());
        Assert.Equal(6, course.GetProperty("completedMeetings").GetInt64());
        Assert.Equal(2, course.GetProperty("incompleteMeetings").GetInt64());
        Assert.Equal(1, course.GetProperty("cancelledMeetings").GetInt64());
        Assert.Equal(2, course.GetProperty("legacyPlaceholders").GetInt64());
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/reports/admin/dashboard")).StatusCode);
        Authenticate("Teacher", _ids.EmptyTeacher);
        Assert.Empty((await Get("/api/reports/teachers/me/summary")).GetProperty("items").EnumerateArray());
        Assert.Empty((await Get("/api/reports/attendance")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Admin_dashboard_separates_global_totals_meetings_records_and_weighted_eligibility()
    {
        var dashboard = await Get("/api/reports/admin/dashboard");
        Assert.Equal(8, dashboard.GetProperty("globalUsers").GetInt64());
        Assert.Equal(4, dashboard.GetProperty("globalStudents").GetInt64());
        Assert.Equal(3, dashboard.GetProperty("globalTeachers").GetInt64());
        Assert.Equal(4, dashboard.GetProperty("globalCourses").GetInt64());
        Assert.Equal(8, dashboard.GetProperty("completedMeetings").GetInt64());
        Assert.Equal(2, dashboard.GetProperty("legacyPlaceholders").GetInt64());
        Assert.Equal(19, dashboard.GetProperty("recordedAttendance").GetInt64());
        Assert.Equal(41.67m, dashboard.GetProperty("attendance").GetProperty("percentage").GetDecimal());
        Assert.Equal(100m, Single(await Get($"/api/reports/teachers/{_ids.OtherTeacher}/summary")).GetProperty("percentage").GetDecimal());
    }

    [Fact]
    public async Task Corrections_change_current_statistics_without_counting_audit_entries_as_sessions()
    {
        var record = Single(await Get($"/api/reports/students/{_ids.Student}/history?attendanceSessionId={_ids.FirstSession}"));
        var id = record.GetProperty("attendanceId").GetInt32();
        var response = await _client.PutAsJsonAsync($"/api/attendances/{id}", new { status = "Absent", expectedVersion = 1, reason = "Register correction." });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var statistics = await Get($"/api/reports/overall?studentId={_ids.Student}&courseId={_ids.Course}");
        Assert.Equal(20m, statistics.GetProperty("percentage").GetDecimal());
        Assert.Equal(5, statistics.GetProperty("totalEligibleSessions").GetInt64());
        using var scope = _factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().AttendanceHistory.CountAsync());
    }

    [Theory]
    [InlineData("attendance?page=0")]
    [InlineData("attendance?pageSize=101")]
    [InlineData("attendance?sortBy=passwordHash")]
    [InlineData("percentages?direction=sideways")]
    [InlineData("overall?fromDate=2026-10-22&toDate=2026-10-20")]
    [InlineData("overall?toDate=9999-12-31")]
    [InlineData("overall?fromDate=not-a-date")]
    [InlineData("overall?studentId=0")]
    [InlineData("attendance?method=Unknown")]
    public async Task Invalid_filters_return_400(string route) => Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/reports/" + route)).StatusCode);

    [Theory]
    [InlineData("policy")]
    [InlineData("attendance")]
    [InlineData("percentages")]
    [InlineData("overall")]
    [InlineData("admin/dashboard")]
    [InlineData("teachers/me/summary")]
    public async Task Anonymous_users_receive_401(string route)
    {
        _client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/reports/" + route)).StatusCode);
    }

    [Fact]
    public async Task Unknown_roles_and_missing_or_stale_account_identities_receive_403()
    {
        foreach (var token in new[] { _factory.Token("Unknown", _ids.Admin.ToString()), _factory.Token("Admin", null),
            _factory.Token("Admin", "999999"), _factory.Token("Admin", _ids.StudentUser.ToString()) })
        {
            _client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/reports/overall")).StatusCode);
        }
    }

    [Fact]
    public async Task Extra_role_claims_cannot_promote_a_current_student_to_the_admin_dashboard()
    {
        var claims = new[]
        {
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, _ids.StudentUser.ToString()),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Student"),
            new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin")
        };
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(SecurityApiFactory.Issuer, SecurityApiFactory.Audience,
            claims, expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(_factory.SigningKey)),
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));
        _client.DefaultRequestHeaders.Authorization = new("Bearer", new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token));
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/reports/admin/dashboard")).StatusCode);
        Assert.Equal(50m, (await Get("/api/reports/overall")).GetProperty("percentage").GetDecimal());
    }

    public void Dispose() { _client.Dispose(); _factory.Dispose(); }
}
