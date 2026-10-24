using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartAttendance.API.Data;
using SmartAttendance.API.Models;
using SmartAttendance.API.Security;
using Xunit;

namespace SmartAttendance.IntegrationTests;

public sealed class InitialAdminProvisioningTests
{
    [Fact]
    public async Task Explicit_provisioning_creates_one_admin_and_rerunning_never_overwrites_it()
    {
        using var factory = new SecurityApiFactory(); factory.ResetEmptyDatabase();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var provisioner = scope.ServiceProvider.GetRequiredService<InitialAdminProvisioner>();
        var settings = new InitialAdminSettings { Name = "Initial Admin", Email = "initial@example.test", Password = SecurityApiFactory.Password };
        Assert.Equal(ProvisioningOutcome.Created, await provisioner.ProvisionAsync(settings, default));
        var user = await db.Users.SingleAsync(); var hash = user.PasswordHash;
        settings.Password = "Different passphrase for testing!";
        Assert.Equal(ProvisioningOutcome.AlreadyProvisioned, await provisioner.ProvisionAsync(settings, default));
        Assert.Equal(hash, (await db.Users.SingleAsync()).PasswordHash); Assert.Equal("Admin", user.Role);
        Assert.NotEqual(SecurityApiFactory.Password, hash); Assert.Equal("INITIAL@EXAMPLE.TEST", user.NormalizedEmail);
    }

    [Fact]
    public async Task Provisioning_never_promotes_an_existing_email_and_invalid_settings_do_not_create_accounts()
    {
        using var factory = new SecurityApiFactory(); factory.ResetEmptyDatabase();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new User { Name = "Student", Email = "existing@example.test", Role = "Student", PasswordHash = "preserved-synthetic-hash" }); await db.SaveChangesAsync();
        var provisioner = scope.ServiceProvider.GetRequiredService<InitialAdminProvisioner>();
        Assert.Equal(ProvisioningOutcome.InvalidSettings, await provisioner.ProvisionAsync(new(), default));
        Assert.Equal(ProvisioningOutcome.EmailConflict, await provisioner.ProvisionAsync(new InitialAdminSettings
        {
            Name = "Admin", Email = "EXISTING@example.test", Password = SecurityApiFactory.Password
        }, default));
        var existing = await db.Users.SingleAsync(); Assert.Equal("Student", existing.Role); Assert.Equal("preserved-synthetic-hash", existing.PasswordHash);
    }

    [Fact]
    public async Task Ordinary_startup_does_not_seed_admin_and_anonymous_http_cannot_bootstrap_one()
    {
        using var factory = new SecurityApiFactory(); factory.ResetEmptyDatabase();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var response = await client.PostAsJsonAsync("/api/users", new { name = "Injected Admin", email = "admin@example.test", role = "Admin", password = SecurityApiFactory.Password });
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        using var scope = factory.Services.CreateScope(); Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync());
    }
}
