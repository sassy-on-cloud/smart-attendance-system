using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SmartAttendance.API.Data;
using SmartAttendance.API.Models;
using Xunit;

namespace SmartAttendance.IntegrationTests;

[Collection("SqlServer")]
public sealed class AttendanceConcurrencyTests(SqlServerFixture fixture)
{
    private static async Task Seed(AppDbContext db)
    {
        await db.Database.MigrateAsync(); // Private fixture-generated database only.
        var admin = new User { Name = "Admin", Role = "Admin", Email = "admin@example.test" };
        var teacher = new User { Name = "Teacher", Role = "Teacher", Email = "teacher@example.test" };
        var user = new User { Name = "Student", Role = "Student", Email = "student@example.test" };
        db.Users.Add(admin); await db.SaveChangesAsync();
        db.Users.Add(teacher); await db.SaveChangesAsync();
        db.Users.Add(user); await db.SaveChangesAsync();
        db.Students.Add(new Student { UserId = user.UserId });
        db.Courses.Add(new Course { CourseCode = "C1" }); await db.SaveChangesAsync();
        db.CourseEnrollments.Add(new CourseEnrollment { StudentId = 1, CourseId = 1 });
        db.TeacherCourseAssignments.Add(new TeacherCourseAssignment { TeacherUserId = 2, CourseId = 1 });
        db.AttendanceSessions.Add(new AttendanceSession { CourseId = 1,
            StartsAtUtc = new DateTime(2026, 10, 20, 4, 0, 0), EndsAtUtc = new DateTime(2026, 10, 20, 5, 0, 0) });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
    }
    private static SecurityApiFactory Host(AppDbContext db, IInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(db.Database.GetConnectionString());
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new SecurityApiFactory { SqlServerTestOptions = options.Options };
    }
    private static HttpClient Client(SecurityApiFactory host, string role = "Admin", string id = "1")
    {
        var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token(role, id));
        return client;
    }
    private static Task<HttpResponseMessage> Mark(HttpClient client) =>
        client.PostAsJsonAsync("/api/attendances", new { studentId = 1, attendanceSessionId = 1, status = "Present" });

    [SqlServerFact]
    public async Task Concurrent_http_submissions_create_exactly_one_attendance_and_one_audit_record()
    {
        await using var db = fixture.NewDatabase(); await Seed(db);
        using var host = Host(db);
        using var admin = Client(host); using var teacher = Client(host, "Teacher", "2");
        var submissions = Enumerable.Range(0, 12).Select(i => Mark(i % 2 == 0 ? admin : teacher)).ToArray();
        var responses = await Task.WhenAll(submissions);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(11, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var attendance = await db.Attendances.SingleAsync();
        var history = await db.AttendanceHistory.SingleAsync();
        Assert.Equal(attendance.RecordedByUserId, history.ActorUserId);
        Assert.Equal("Manual", attendance.Method);
        Assert.Equal(1, attendance.Version);
    }

    [SqlServerFact]
    public async Task Concurrent_corrections_with_same_expected_version_have_one_winner_and_one_history_entry()
    {
        await using var db = fixture.NewDatabase(); await Seed(db);
        using var host = Host(db); using var first = Client(host); using var second = Client(host, "Teacher", "2");
        var mark = await Mark(first); Assert.Equal(HttpStatusCode.Created, mark.StatusCode);
        var id = (await mark.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("attendanceId").GetInt32();
        var responses = await Task.WhenAll(
            first.PutAsJsonAsync($"/api/attendances/{id}", new { status = "Absent", expectedVersion = 1, reason = "Admin review." }),
            second.PutAsJsonAsync($"/api/attendances/{id}", new { status = "Late", expectedVersion = 1, reason = "Teacher review." }));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var attendance = await db.Attendances.SingleAsync();
        Assert.Equal(2, attendance.Version); Assert.Equal(2, await db.AttendanceHistory.CountAsync());
        var corrected = await db.AttendanceHistory.SingleAsync(h => h.Version == 2);
        Assert.Equal(attendance.Status, corrected.Status);
        Assert.Equal(attendance.LastModifiedByUserId, corrected.ActorUserId);
        Assert.Equal("Present", corrected.PreviousStatus);
    }

    [SqlServerFact]
    public async Task Cancellation_racing_a_mark_has_a_consistent_serial_order()
    {
        await using var db = fixture.NewDatabase(); await Seed(db);
        using var host = Host(db); using var marker = Client(host); using var manager = Client(host);
        var responses = await Task.WhenAll(Mark(marker), manager.PutAsJsonAsync("/api/attendancesessions/1", new
        {
            startsAt = "2026-10-20T04:00:00Z", endsAt = "2026-10-20T05:00:00Z", state = "Cancelled", expectedVersion = 1,
            reason = "Class cancelled."
        }));
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);
        Assert.Contains(responses[0].StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.Conflict });
        Assert.Equal("Cancelled", (await db.AttendanceSessions.SingleAsync()).State);
        var marks = await db.Attendances.CountAsync();
        Assert.Equal(responses[0].StatusCode == HttpStatusCode.Created ? 1 : 0, marks);
        Assert.Equal(marks, await db.AttendanceHistory.CountAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await Mark(marker)).StatusCode);
    }

    [SqlServerFact]
    public async Task Concurrent_session_edits_reject_lost_updates_and_audit_only_the_winner()
    {
        await using var db = fixture.NewDatabase(); await Seed(db);
        using var host = Host(db); using var first = Client(host); using var second = Client(host);
        Task<HttpResponseMessage> Edit(HttpClient client, string startsAt, string endsAt) =>
            client.PutAsJsonAsync("/api/attendancesessions/1", new { startsAt, endsAt, state = "Open", expectedVersion = 1, reason = "Reschedule." });
        var responses = await Task.WhenAll(Edit(first, "2026-10-20T06:00:00Z", "2026-10-20T07:00:00Z"),
            Edit(second, "2026-10-20T08:00:00Z", "2026-10-20T09:00:00Z"));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(2, (await db.AttendanceSessions.SingleAsync()).Version);
        Assert.Equal(1, await db.AttendanceSessionHistory.CountAsync());
    }

    [SqlServerFact]
    public async Task Rescheduling_racing_a_mark_cannot_reinterpret_an_existing_attendance()
    {
        await using var db = fixture.NewDatabase(); await Seed(db);
        using var host = Host(db); using var marker = Client(host); using var manager = Client(host);
        var responses = await Task.WhenAll(Mark(marker), manager.PutAsJsonAsync("/api/attendancesessions/1", new
        {
            startsAt = "2026-10-20T06:00:00Z", endsAt = "2026-10-20T07:00:00Z", state = "Open", expectedVersion = 1, reason = "Reschedule."
        }));
        Assert.Equal(HttpStatusCode.Created, responses[0].StatusCode);
        Assert.Contains(responses[1].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        var session = await db.AttendanceSessions.SingleAsync();
        Assert.Equal(responses[1].StatusCode == HttpStatusCode.OK ? 6 : 4, session.StartsAtUtc!.Value.Hour);
        Assert.Equal(1, await db.Attendances.CountAsync());
        Assert.Equal(responses[1].StatusCode == HttpStatusCode.OK ? 1 : 0, await db.AttendanceSessionHistory.CountAsync());
    }

    [SqlServerFact]
    public async Task Failure_after_saving_attendance_and_history_rolls_back_the_entire_transaction()
    {
        await using var db = fixture.NewDatabase(); await Seed(db);
        using var host = Host(db, new FailAfterAttendanceSaveInterceptor()); using var client = Client(host);
        Assert.Equal(HttpStatusCode.InternalServerError, (await Mark(client)).StatusCode);
        Assert.Equal(0, await db.Attendances.CountAsync()); Assert.Equal(0, await db.AttendanceHistory.CountAsync());
    }

    [SqlServerFact]
    public async Task Audit_actor_foreign_keys_and_versions_are_enforced_in_sql_server()
    {
        await using var db = fixture.NewDatabase(); await Seed(db);
        using var host = Host(db); using var client = Client(host); Assert.Equal(HttpStatusCode.Created, (await Mark(client)).StatusCode);
        var attendance = await db.Attendances.SingleAsync();
        db.ChangeTracker.Clear();
        db.AttendanceHistory.Add(new AttendanceHistory { AttendanceId = attendance.AttendanceId, ActorUserId = 999,
            Version = 2, ChangeType = "Corrected", Status = "Absent", Method = "Manual", Reason = "Synthetic." });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        db.AttendanceHistory.Add(new AttendanceHistory { AttendanceId = attendance.AttendanceId, ActorUserId = 1,
            Version = 1, ChangeType = "Created", Status = "Present", Method = "Manual", Reason = "Duplicate." });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [SqlServerFact]
    public async Task Additive_upgrade_keeps_existing_records_and_does_not_fabricate_actors()
    {
        await using var db = fixture.NewDatabase();
        await db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20261024061936_SessionAttendanceIntegrity");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Users (Name,Email,PasswordHash,Role) VALUES ('Student','old@example.test','synthetic','Student');
            INSERT INTO Students (RollNumber,Department,Semester,UserId) VALUES ('S1','Computing',1,1);
            INSERT INTO Courses (CourseName,CourseCode) VALUES ('Course','C1');
            INSERT INTO CourseEnrollments (StudentId,CourseId,IsLegacyInferred) VALUES (1,1,0);
            INSERT INTO AttendanceSessions (CourseId,StartsAtUtc,EndsAtUtc,IsLegacy,LegacyAttendanceId) VALUES (1,'2026-10-20T04:00:00','2026-10-20T05:00:00',0,NULL);
            INSERT INTO Attendances (StudentId,CourseId,AttendanceSessionId,Date,Status,Method) VALUES (1,1,1,'2026-10-20T04:01:00','Present','Manual');
            """);
        await db.Database.MigrateAsync();
        var attendance = await db.Attendances.SingleAsync(); Assert.Equal(1, attendance.AttendanceId);
        Assert.Equal("Present", attendance.Status); Assert.Equal(new DateTime(2026,10,20,4,1,0), attendance.Date);
        Assert.Null(attendance.RecordedByUserId); Assert.Null(attendance.LastModifiedByUserId); Assert.Equal(1, attendance.Version);
        var session = await db.AttendanceSessions.SingleAsync(); Assert.Null(session.CreatedByUserId);
        Assert.Equal("Open", session.State); Assert.Equal(1, session.Version);
        Assert.Empty(await db.AttendanceHistory.ToListAsync()); Assert.Empty(await db.AttendanceSessionHistory.ToListAsync());
    }
}

public sealed class FailAfterAttendanceSaveInterceptor : SaveChangesInterceptor
{
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<AttendanceHistory>().Any())
            throw new InvalidOperationException("Synthetic failure before the attendance transaction commits.");
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }
}
