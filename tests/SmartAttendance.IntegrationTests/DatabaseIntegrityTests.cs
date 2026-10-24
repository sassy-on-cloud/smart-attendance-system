using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartAttendance.API.Data;
using SmartAttendance.API.Models;
using Xunit;

namespace SmartAttendance.IntegrationTests;

public sealed class DatabaseIntegrityTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private static readonly DateTime MeetingStart = new(2026, 10, 20, 4, 0, 0, DateTimeKind.Utc);

    private async Task<AppDbContext> Seed()
    {
        var db = fixture.NewDatabase();
        await db.Database.MigrateAsync(); // Only the fixture's new random database inside its own container.
        var studentUser = new User { Name = "Student", Email = "student@example.test", Role = "Student" };
        var teacher = new User { Name = "Teacher", Email = "teacher@example.test", Role = "Teacher" };
        db.Users.Add(studentUser);
        await db.SaveChangesAsync();
        db.Users.Add(teacher);
        await db.SaveChangesAsync();
        db.Students.Add(new Student { UserId = studentUser.UserId });
        db.Courses.Add(new Course { CourseCode = "C1" });
        await db.SaveChangesAsync();
        db.Courses.Add(new Course { CourseCode = "C2" });
        await db.SaveChangesAsync();
        db.CourseEnrollments.Add(new CourseEnrollment { StudentId = 1, CourseId = 1 });
        db.TeacherCourseAssignments.Add(new TeacherCourseAssignment { TeacherUserId = teacher.UserId, CourseId = 1 });
        db.AttendanceSessions.Add(new AttendanceSession { CourseId = 1, StartsAtUtc = MeetingStart, EndsAtUtc = MeetingStart.AddHours(1) });
        await db.SaveChangesAsync();
        return db;
    }

    private static Attendance Record(int studentId = 1, int courseId = 1, int sessionId = 1) => new()
    {
        StudentId = studentId, CourseId = courseId, AttendanceSessionId = sessionId,
        Date = MeetingStart, Status = AttendanceStatuses.Present, Method = AttendanceMethods.Manual
    };

    [SqlServerFact]
    public async Task One_record_per_student_session_and_multiple_same_day_sessions()
    {
        await using var db = await Seed();
        db.Attendances.Add(Record());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.Attendances.Add(Record());
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(((SqlException)duplicate.InnerException!).Number, new[] { 2601, 2627 });
        db.ChangeTracker.Clear();
        db.AttendanceSessions.Add(new AttendanceSession { CourseId = 1, StartsAtUtc = MeetingStart.AddHours(3), EndsAtUtc = MeetingStart.AddHours(4) });
        await db.SaveChangesAsync();
        db.Attendances.Add(Record(sessionId: 2));
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.Attendances.CountAsync());
    }

    [SqlServerFact]
    public async Task Session_course_and_enrollment_must_both_match()
    {
        await using var db = await Seed();
        // Student is enrolled in course 2 too; a course-1 session still cannot be marked as course 2.
        db.CourseEnrollments.Add(new CourseEnrollment { StudentId = 1, CourseId = 2 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.Attendances.Add(Record(courseId: 2));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Users.Add(new User { Role = "Student" });
        await db.SaveChangesAsync();
        db.Students.Add(new Student { UserId = 3 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.Attendances.Add(Record(studentId: 2)); // Exists but is not enrolled.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [SqlServerFact]
    public async Task Duplicate_enrollment_assignment_and_student_account_are_rejected()
    {
        await using var db = await Seed();
        db.ChangeTracker.Clear();
        db.CourseEnrollments.Add(new CourseEnrollment { StudentId = 1, CourseId = 1 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.TeacherCourseAssignments.Add(new TeacherCourseAssignment { TeacherUserId = 2, CourseId = 1 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Students.Add(new Student { UserId = 1 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [SqlServerFact]
    public async Task Invalid_foreign_keys_and_status_method_values_are_rejected()
    {
        await using var db = await Seed();
        db.CourseEnrollments.Add(new CourseEnrollment { StudentId = 999, CourseId = 1 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.TeacherCourseAssignments.Add(new TeacherCourseAssignment { TeacherUserId = 999, CourseId = 1 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Attendances.Add(Record(sessionId: 999));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var attendance = Record(); attendance.Status = "Invented";
        db.Attendances.Add(attendance);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        attendance = Record(); attendance.Method = "Invented";
        db.Attendances.Add(attendance);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [SqlServerFact]
    public async Task Meeting_times_and_history_deletion_are_protected()
    {
        await using var db = await Seed();
        db.AttendanceSessions.Add(new AttendanceSession { CourseId = 1, StartsAtUtc = MeetingStart, EndsAtUtc = MeetingStart });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.AttendanceSessions.Add(new AttendanceSession { CourseId = 1 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Attendances.Add(Record()); await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        // Raw SQL bypasses EF fixup, exercising actual NO ACTION database constraints.
        foreach (var sql in new[] { "DELETE FROM Users WHERE UserId=1", "DELETE FROM Students WHERE StudentId=1",
            "DELETE FROM Courses WHERE CourseId=1", "DELETE FROM AttendanceSessions WHERE AttendanceSessionId=1",
            "DELETE FROM CourseEnrollments WHERE StudentId=1 AND CourseId=1" })
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql));
        Assert.Equal(1, await db.Attendances.CountAsync());
    }

    [SqlServerFact]
    public async Task Concurrent_writers_are_serialized_by_unique_constraint()
    {
        await using var db = await Seed();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(db.Database.GetConnectionString()).Options;
        async Task<bool> Insert()
        {
            await using var writer = new AppDbContext(options);
            writer.Attendances.Add(Record());
            try { await writer.SaveChangesAsync(); return true; }
            catch (DbUpdateException error) when (error.InnerException is SqlException sql && sql.Number is 2601 or 2627) { return false; }
        }
        var results = await Task.WhenAll(Insert(), Insert());
        Assert.Single(results, success => success);
        Assert.Equal(1, await db.Attendances.CountAsync());
    }

    [SqlServerFact]
    public async Task Legacy_upgrade_preserves_every_original_row_and_value_without_grouping_dates()
    {
        await using var db = fixture.NewDatabase();
        await db.GetService<IMigrator>().MigrateAsync("20260825122101_AddAttendanceMethod");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Users (Name,Email,PasswordHash,Role) VALUES ('Student','legacy@example.test','synthetic','Student');
            INSERT INTO Students (RollNumber,Department,Semester,UserId) VALUES ('L1','Computing',1,1);
            INSERT INTO Courses (CourseName,CourseCode) VALUES ('Legacy','L1');
            INSERT INTO Attendances (StudentId,CourseId,Date,Status,Method) VALUES
              (1,1,'2026-10-20T04:00:00','Present','Manual'),
              (1,1,'2026-10-20T04:00:00','Present',''),
              (1,1,'2026-10-20T04:00:00','custom legacy status longer than twenty characters','old custom method longer than twenty characters');
            """);
        await db.Database.MigrateAsync();
        var rows = await db.Attendances.AsNoTracking().OrderBy(a => a.AttendanceId).ToListAsync();
        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(a => a.AttendanceId));
        Assert.All(rows, row => { Assert.Equal(1, row.StudentId); Assert.Equal(1, row.CourseId); Assert.Equal(MeetingStart, row.Date); });
        Assert.Equal(3, rows.Select(a => a.AttendanceSessionId).Distinct().Count());
        Assert.Equal("", rows[1].LegacyMethod);
        Assert.Equal("LegacyUnknown", rows[1].Method);
        Assert.Equal("custom legacy status longer than twenty characters", rows[2].LegacyStatus);
        Assert.Equal("old custom method longer than twenty characters", rows[2].LegacyMethod);
        Assert.Equal("Unverified", rows[2].Status);
        Assert.All(await db.AttendanceSessions.ToListAsync(), session => { Assert.True(session.IsLegacy); Assert.Null(session.StartsAtUtc); Assert.Null(session.EndsAtUtc); });
        Assert.True((await db.CourseEnrollments.SingleAsync()).IsLegacyInferred);
        Assert.Empty(await db.TeacherCourseAssignments.ToListAsync());
        var newSession = new AttendanceSession { CourseId = 1, StartsAtUtc = MeetingStart, EndsAtUtc = MeetingStart.AddHours(1) };
        db.AttendanceSessions.Add(newSession); await db.SaveChangesAsync();
        Assert.True(newSession.AttendanceSessionId > 3);
    }

    [SqlServerFact]
    public async Task Duplicate_profiles_abort_before_schema_or_data_changes()
    {
        await using var db = fixture.NewDatabase();
        await db.GetService<IMigrator>().MigrateAsync("20260825122101_AddAttendanceMethod");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Users (Name,Email,PasswordHash,Role) VALUES ('Student','duplicate@example.test','synthetic','Student');
            INSERT INTO Students (RollNumber,Department,Semester,UserId) VALUES ('L1','Computing',1,1),('L2','Computing',1,1);
            """);
        var exception = await Assert.ThrowsAsync<SqlException>(() => db.Database.MigrateAsync());
        Assert.Equal(51000, exception.Number);
        Assert.Equal(2, await db.Students.CountAsync());
        Assert.Equal(2, (await db.Database.GetAppliedMigrationsAsync()).Count());
        var tableCount = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.tables WHERE name='AttendanceSessions'").SingleAsync();
        Assert.Equal(0, tableCount);
    }

    [SqlServerFact]
    public async Task Generated_idempotent_upgrade_script_is_safe_to_apply_twice_in_test_database()
    {
        await using var db = fixture.NewDatabase();
        await db.Database.EnsureCreatedAsync(); // A different temporary database is used below for script installation.
        await using var scripted = fixture.NewDatabase();
        await using var master = new SqlConnection(db.Database.GetConnectionString());
        await master.OpenAsync();
        var databaseName = new SqlConnectionStringBuilder(scripted.Database.GetConnectionString()).InitialCatalog;
        Assert.StartsWith("AttendanceTests_", databaseName);
        await using (var create = master.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE [{databaseName}]";
            await create.ExecuteNonQueryAsync();
        }
        var script = scripted.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        await using var connection = new SqlConnection(scripted.Database.GetConnectionString());
        await connection.OpenAsync();
        for (var repeat = 0; repeat < 2; repeat++)
        {
            foreach (var batch in System.Text.RegularExpressions.Regex.Split(script, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline))
            {
                if (string.IsNullOrWhiteSpace(batch)) continue;
                await using var command = connection.CreateCommand(); command.CommandText = batch;
                await command.ExecuteNonQueryAsync();
            }
        }
        Assert.Equal(3, (await scripted.Database.GetAppliedMigrationsAsync()).Count());
    }
}
