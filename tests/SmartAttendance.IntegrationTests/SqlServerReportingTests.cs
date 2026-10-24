using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartAttendance.API.Data;
using SmartAttendance.API.Models;
using Xunit;

namespace SmartAttendance.IntegrationTests;

[Collection("SqlServer")]
public sealed class SqlServerReportingTests(SqlServerFixture fixture)
{
    private static SecurityApiFactory Host(AppDbContext db, ReportQueryProbe? probe = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(db.Database.GetConnectionString());
        if (probe is not null) options.AddInterceptors(probe);
        return new() { SqlServerTestOptions = options.Options, TestClock = new FixedReportClock() };
    }
    private static HttpClient Client(SecurityApiFactory host, string role, int userId)
    {
        var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(role, userId.ToString()));
        return client;
    }
    private static async Task<JsonElement> Get(HttpClient client, string route)
    {
        var response = await client.GetAsync(route); var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{route}: {response.StatusCode}: {body}");
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    [SqlServerFact]
    public async Task Sql_server_executes_all_reporting_aggregates_and_teacher_dashboard_queries()
    {
        await using var db = fixture.NewDatabase(); await db.Database.MigrateAsync();
        var ids = await ReportingTestData.SeedAsync(db);
        using var host = Host(db); using var admin = Client(host, "Admin", ids.Admin); using var teacher = Client(host, "Teacher", ids.Teacher);
        using var student = Client(host, "Student", ids.StudentUser);
        var overall = await Get(admin, "/api/reports/overall");
        Assert.Equal(5, overall.GetProperty("presentSessions").GetInt64());
        Assert.Equal(12, overall.GetProperty("totalEligibleSessions").GetInt64());
        Assert.Equal(41.67m, overall.GetProperty("percentage").GetDecimal());
        var one = Assert.Single((await Get(student, $"/api/reports/percentages?courseId={ids.Course}")).GetProperty("items").EnumerateArray());
        Assert.Equal(40m, one.GetProperty("percentage").GetDecimal()); Assert.Equal(1, one.GetProperty("unmarkedSessions").GetInt64());
        var courses = await Get(teacher, "/api/reports/teachers/me/summary?sortBy=percentage&direction=desc");
        Assert.Equal(3, courses.GetProperty("items").GetArrayLength());
        Assert.Equal(36.36m, (await Get(teacher, "/api/reports/overall")).GetProperty("percentage").GetDecimal());
        Assert.Equal(41.67m, (await Get(admin, "/api/reports/admin/dashboard")).GetProperty("attendance").GetProperty("percentage").GetDecimal());
        Assert.Equal(3, (await Get(student, "/api/reports/percentages")).GetProperty("totalCount").GetInt64());
    }

    [SqlServerFact]
    public async Task Sql_server_filters_sorts_pages_and_keeps_unmarked_sessions_in_the_denominator()
    {
        await using var db = fixture.NewDatabase(); await db.Database.MigrateAsync(); var ids = await ReportingTestData.SeedAsync(db);
        using var host = Host(db); using var student = Client(host, "Student", ids.StudentUser);
        var dated = await Get(student, $"/api/reports/overall?courseId={ids.Course}&fromDate=2026-10-20&toDate=2026-10-22");
        Assert.Equal(3, dated.GetProperty("totalEligibleSessions").GetInt64()); Assert.Equal(66.67m, dated.GetProperty("percentage").GetDecimal());
        var pages = await Get(student, "/api/reports/percentages?sortBy=percentage&direction=asc&page=1&pageSize=1");
        Assert.Equal(3, pages.GetProperty("totalCount").GetInt64()); Assert.Equal(JsonValueKind.Null, Assert.Single(pages.GetProperty("items").EnumerateArray()).GetProperty("percentage").ValueKind);
        var records = await Get(student, $"/api/reports/courses/{ids.Course}/records?fromDate=2026-10-20&toDate=2026-10-20&sortBy=sessionDate&direction=asc&pageSize=100");
        Assert.Equal(4, records.GetProperty("totalCount").GetInt64());
        Assert.All(records.GetProperty("items").EnumerateArray(), r => Assert.False(r.GetProperty("isLegacy").GetBoolean()));
        var session = await Get(student, $"/api/reports/sessions/{ids.SecondSession}/records"); Assert.Single(session.GetProperty("items").EnumerateArray());
    }

    [SqlServerFact]
    public async Task Sql_server_queries_aggregate_and_page_on_server_without_loading_password_hashes_or_n_plus_one()
    {
        await using var db = fixture.NewDatabase(); await db.Database.MigrateAsync(); var ids = await ReportingTestData.SeedAsync(db);
        var probe = new ReportQueryProbe(); using var host = Host(db, probe); using var admin = Client(host, "Admin", ids.Admin);
        var page = await Get(admin, "/api/reports/percentages?pageSize=1&sortBy=percentage");
        Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.InRange(probe.Commands.Count, 1, 4);
        Assert.Contains(probe.Commands, sql => sql.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(probe.Commands, sql => sql.Contains("FETCH NEXT", StringComparison.OrdinalIgnoreCase));
        Assert.All(probe.Commands, sql => Assert.DoesNotContain("PasswordHash", sql, StringComparison.OrdinalIgnoreCase));
    }

    [SqlServerFact]
    public async Task Sql_server_empty_database_reports_zero_counts_and_null_percentage()
    {
        await using var db = fixture.NewDatabase(); await db.Database.MigrateAsync();
        var adminUser = new User { Role = "Admin", Name = "Synthetic Admin" }; db.Users.Add(adminUser); await db.SaveChangesAsync();
        using var host = Host(db); using var admin = Client(host, "Admin", adminUser.UserId);
        var overall = await Get(admin, "/api/reports/overall"); Assert.Equal(0, overall.GetProperty("totalEligibleSessions").GetInt64());
        Assert.Equal(JsonValueKind.Null, overall.GetProperty("percentage").ValueKind);
        Assert.Empty((await Get(admin, "/api/reports/attendance")).GetProperty("items").EnumerateArray());
        Assert.Empty((await Get(admin, "/api/reports/percentages")).GetProperty("items").EnumerateArray());
        var dashboard = await Get(admin, "/api/reports/admin/dashboard"); Assert.Equal(0, dashboard.GetProperty("completedMeetings").GetInt64());
        Assert.Equal(0, dashboard.GetProperty("globalStudents").GetInt64());
    }

    [SqlServerFact]
    public async Task Closed_session_end_boundary_uses_fixed_asof_and_future_sessions_stay_excluded()
    {
        await using var db = fixture.NewDatabase(); await db.Database.MigrateAsync(); var ids = await ReportingTestData.SeedAsync(db);
        var meeting = await db.AttendanceSessions.FindAsync(ids.FutureSession);
        meeting!.StartsAtUtc = ReportingTestData.Now.AddHours(-1).UtcDateTime; meeting.EndsAtUtc = ReportingTestData.Now.UtcDateTime;
        await db.SaveChangesAsync();
        using var host = Host(db); using var student = Client(host, "Student", ids.StudentUser);
        var exact = await Get(student, $"/api/reports/overall?courseId={ids.Course}");
        Assert.Equal(50m, exact.GetProperty("percentage").GetDecimal());
        meeting.EndsAtUtc = ReportingTestData.Now.AddTicks(1).UtcDateTime; await db.SaveChangesAsync();
        var future = await Get(student, $"/api/reports/overall?courseId={ids.Course}"); Assert.Equal(40m, future.GetProperty("percentage").GetDecimal());
    }

    [SqlServerFact]
    public async Task Sql_server_reports_use_current_corrections_and_respect_teacher_and_student_scope()
    {
        await using var db = fixture.NewDatabase(); await db.Database.MigrateAsync(); var ids = await ReportingTestData.SeedAsync(db);
        using var host = Host(db); using var admin = Client(host, "Admin", ids.Admin);
        using var teacher = Client(host, "Teacher", ids.Teacher); using var student = Client(host, "Student", ids.StudentUser);
        var record = await db.Attendances.SingleAsync(a => a.StudentId == ids.Student && a.AttendanceSessionId == ids.FirstSession);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/attendances/{record.AttendanceId}", new { status = "Absent", expectedVersion = 1, reason = "Synthetic register correction." })).StatusCode);
        var updated = await Get(student, $"/api/reports/overall?courseId={ids.Course}"); Assert.Equal(20m, updated.GetProperty("percentage").GetDecimal());
        Assert.Equal(1, await db.AttendanceHistory.CountAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync($"/api/reports/students/{ids.OtherStudent}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await teacher.GetAsync($"/api/reports/percentages?courseId={ids.HiddenCourse}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync("/api/reports/admin/dashboard")).StatusCode);
    }
}

public sealed class ReportQueryProbe : DbCommandInterceptor
{
    public List<string> Commands { get; } = [];
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Commands.Add(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
