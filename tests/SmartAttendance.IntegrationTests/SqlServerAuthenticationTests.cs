using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using SmartAttendance.API.Data;
using SmartAttendance.API.Models;
using SmartAttendance.API.Security;
using Xunit;

namespace SmartAttendance.IntegrationTests;

[Collection("SqlServer")]
public sealed class SqlServerAuthenticationTests(SqlServerFixture fixture)
{
    private static SecurityApiFactory Host(AppDbContext db) => new()
    {
        SqlServerTestOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(db.Database.GetConnectionString()).Options
    };
    private static async Task<int> SeedAdmin(AppDbContext db)
    {
        await db.Database.MigrateAsync(); var user = new User { Name = "Admin", Email = "admin@example.test", Role = "Admin" };
        db.Users.Add(user); await db.SaveChangesAsync(); return user.UserId;
    }

    [SqlServerFact]
    public async Task Concurrent_case_variant_registrations_have_one_created_account_and_safe_409_conflicts()
    {
        await using var db = fixture.NewDatabase(); var adminId = await SeedAdmin(db);
        using var host = Host(db); using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token("Admin", adminId.ToString()));
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => client.PostAsJsonAsync("/api/users", new
        {
            name = "New Student", email = i % 2 == 0 ? "New@example.test" : "new@EXAMPLE.TEST", role = "Student", password = SecurityApiFactory.Password
        })));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(7, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await db.Users.CountAsync(u => u.NormalizedEmail == "NEW@EXAMPLE.TEST"));
        db.Users.Add(new User { Name = "Duplicate", Email = "NEW@example.test", Role = "Student" });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(((SqlException)error.InnerException!).Number, new[] { 2601, 2627 });
    }

    [SqlServerFact]
    public async Task Concurrent_login_guesses_are_reserved_atomically_before_password_verification()
    {
        await using var db = fixture.NewDatabase(); await SeedAdmin(db);
        var user = new User { Email = "student@example.test", Name = "Student", Role = "Student" };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, SecurityApiFactory.Password); db.Users.Add(user); await db.SaveChangesAsync();
        using var host = Host(db); using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => client.PostAsJsonAsync("/api/users/login", new
        {
            email = i % 2 == 0 ? "student@example.test" : "STUDENT@example.test", password = "incorrect-password"
        })));
        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
    }

    [SqlServerFact]
    public async Task Concurrent_first_admin_commands_with_different_emails_create_only_one_admin()
    {
        await using var db = fixture.NewDatabase(); await db.Database.MigrateAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(db.Database.GetConnectionString()).Options;
        async Task<ProvisioningOutcome> Provision(int attempt)
        {
            await using var context = new AppDbContext(options);
            var service = new InitialAdminProvisioner(context, new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount = 210_000 })));
            return await service.ProvisionAsync(new InitialAdminSettings { Name = "Initial Admin", Email = $"initial-{attempt}@example.test", Password = SecurityApiFactory.Password }, default);
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(Provision));
        Assert.Single(results, r => r == ProvisioningOutcome.Created);
        Assert.Equal(5, results.Count(r => r == ProvisioningOutcome.AlreadyProvisioned));
        Assert.Equal(1, await db.Users.CountAsync()); Assert.Equal(1, await db.Users.CountAsync(u => u.Role == "Admin"));
    }

    [SqlServerFact]
    public async Task Email_upgrade_preserves_existing_display_values_ids_and_password_hashes()
    {
        await using var db = fixture.NewDatabase(); await db.GetService<IMigrator>().MigrateAsync("20261024065458_AttendanceManagementAudit");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Users (Name,Email,PasswordHash,Role) VALUES
            ('Student',' Mixed@Example.Test ','preserved-synthetic-hash','Student'),('Historical empty','','old-empty-hash','Teacher');
            """);
        await db.Database.MigrateAsync();
        var users = await db.Users.OrderBy(u => u.UserId).ToListAsync();
        Assert.Equal(1, users[0].UserId); Assert.Equal(" Mixed@Example.Test ", users[0].Email);
        Assert.Equal("MIXED@EXAMPLE.TEST", users[0].NormalizedEmail); Assert.Equal("preserved-synthetic-hash", users[0].PasswordHash);
        Assert.Null(users[1].NormalizedEmail); Assert.Equal("Teacher", users[1].Role);
    }

    [SqlServerFact]
    public async Task Case_variant_duplicate_emails_abort_upgrade_before_any_schema_or_data_changes()
    {
        await using var db = fixture.NewDatabase(); await db.GetService<IMigrator>().MigrateAsync("20261024065458_AttendanceManagementAudit");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Users (Name,Email,PasswordHash,Role) VALUES
            ('One','duplicate@example.test','synthetic-one','Student'),('Two',' DUPLICATE@example.test ','synthetic-two','Teacher');
            """);
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.MigrateAsync()); Assert.Equal(51021, error.Number);
        Assert.Equal(4, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Users").SingleAsync());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.columns WHERE object_id=OBJECT_ID(N'Users') AND name='NormalizedEmail'").SingleAsync());
    }

    [SqlServerFact]
    public async Task Overlong_email_key_aborts_upgrade_without_truncation()
    {
        await using var db = fixture.NewDatabase(); await db.GetService<IMigrator>().MigrateAsync("20261024065458_AttendanceManagementAudit");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Users (Name,Email,PasswordHash,Role) VALUES ('Long',REPLICATE('a',255),'synthetic','Student');");
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.MigrateAsync()); Assert.Equal(51020, error.Number);
        Assert.Equal(4, (await db.Database.GetAppliedMigrationsAsync()).Count());
    }
}
