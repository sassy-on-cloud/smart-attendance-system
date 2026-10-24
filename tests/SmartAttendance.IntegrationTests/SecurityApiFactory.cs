using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SmartAttendance.API.Data;
using SmartAttendance.API.Models;

namespace SmartAttendance.IntegrationTests;

public sealed class SecurityApiFactory : WebApplicationFactory<Program>
{
    public const string Password = "Synthetic-test-password-42!";
    public const string Issuer = "attendance-tests";
    public const string Audience = "attendance-test-client";
    public const string FailureSecret = "synthetic-credential-must-never-be-in-a-response";
    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    public DbContextOptions<AppDbContext>? SqlServerTestOptions { get; init; }
    public TimeProvider? TestClock { get; init; }
    public string EnvironmentName { get; set; } = "Testing";
    private static readonly string SeedHash = new PasswordHasher<User>().HashPassword(new User(), Password);
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    public LeakProbeInterceptor Probe { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            // Replace, rather than inherit, the user's local configuration/secrets.
            configuration.Sources.Clear();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = SigningKey,
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["ConnectionStrings:DefaultConnection"] = "unused-test-only-connection"
            });
        });
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            if (TestClock is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(TestClock);
            }
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            if (SqlServerTestOptions is not null)
                services.AddScoped(_ => new AppDbContext(SqlServerTestOptions));
            else
            {
                _connection.Open();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection).AddInterceptors(Probe));
            }
        });
    }

    public void ResetDatabase()
    {
        ResetEmptyDatabase();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = new[]
        {
            new User { UserId = 11, Name = "Admin", Email = "admin@example.test", Role = "Admin" },
            new User { UserId = 22, Name = "Teacher", Email = "teacher@example.test", Role = "Teacher" },
            new User { UserId = 101, Name = "Student One", Email = "one@example.test", Role = "Student" },
            new User { UserId = 202, Name = "Student Two", Email = "two@example.test", Role = "Student" },
            new User { UserId = 303, Name = "Unlinked Student", Email = "unlinked@example.test", Role = "Student" }
        };
        foreach (var user in users)
            user.PasswordHash = SeedHash;
        db.Users.AddRange(users);
        // Student primary keys deliberately differ from account IDs.
        db.Students.AddRange(
            new Student { StudentId = 1, UserId = 101, RollNumber = "CS-01", Department = "Computing", Semester = 1 },
            new Student { StudentId = 2, UserId = 202, RollNumber = "CS-02", Department = "Computing", Semester = 1 });
        db.Courses.Add(new Course { CourseId = 1, CourseCode = "CS101", CourseName = "Programming" });
        db.CourseEnrollments.AddRange(
            new CourseEnrollment { StudentId = 1, CourseId = 1 },
            new CourseEnrollment { StudentId = 2, CourseId = 1 });
        db.TeacherCourseAssignments.Add(new TeacherCourseAssignment { TeacherUserId = 22, CourseId = 1 });
        for (var sessionId = 1; sessionId <= 2; sessionId++)
            db.AttendanceSessions.Add(new AttendanceSession
            {
                AttendanceSessionId = sessionId, CourseId = 1,
                StartsAtUtc = new DateTime(2026, 10, 20, sessionId + 3, 0, 0, DateTimeKind.Utc),
                EndsAtUtc = new DateTime(2026, 10, 20, sessionId + 4, 0, 0, DateTimeKind.Utc)
            });
        db.Attendances.AddRange(
            new Attendance { AttendanceId = 1, AttendanceSessionId = 1, StudentId = 1, CourseId = 1, Date = new DateTime(2026, 10, 20, 4, 0, 0, DateTimeKind.Utc), Status = "Present", Method = "Manual" },
            new Attendance { AttendanceId = 2, AttendanceSessionId = 1, StudentId = 2, CourseId = 1, Date = new DateTime(2026, 10, 20, 4, 0, 0, DateTimeKind.Utc), Status = "Absent", Method = "Manual" });
        db.SaveChanges();
    }

    public void ResetEmptyDatabase()
    {
        Probe.ThrowOnRead = false;
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Fail closed: only this open in-memory connection may be touched.
        if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite" ||
            !ReferenceEquals(db.Database.GetDbConnection(), _connection) ||
            new SqliteConnectionStringBuilder(_connection.ConnectionString).DataSource != ":memory:")
            throw new InvalidOperationException("Security tests require the private in-memory database.");
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
    }

    public string Token(string role, string? userId = "101", string? issuer = null,
        DateTime? expires = null, string? key = null, string? audience = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (userId is not null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        var token = new JwtSecurityToken(issuer ?? Issuer, audience ?? Audience, claims,
            expires: expires ?? DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key ?? SigningKey)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}

public sealed class LeakProbeInterceptor : DbCommandInterceptor
{
    public bool ThrowOnRead { get; set; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (ThrowOnRead)
            throw new InvalidOperationException(SecurityApiFactory.FailureSecret);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
