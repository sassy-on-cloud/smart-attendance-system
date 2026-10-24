using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;
using SmartAttendance.API.Models;

namespace SmartAttendance.API.Security;

public sealed class InitialAdminSettings
{
    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = string.Empty;
    [Required, PasswordPolicy] public string Password { get; set; } = string.Empty;
}
public enum ProvisioningOutcome { Created, AlreadyProvisioned, EmailConflict, InvalidSettings }

public sealed class InitialAdminProvisioner(AppDbContext context, IPasswordHasher<User> hasher)
{
    public async Task<ProvisioningOutcome> ProvisionAsync(InitialAdminSettings settings, CancellationToken ct)
    {
        if (!Validator.TryValidateObject(settings, new ValidationContext(settings), [], true)) return ProvisioningOutcome.InvalidSettings;
        var normalized = EmailIdentity.Normalize(settings.Email);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (context.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            // Per-database transaction-owned lock serializes first-admin operations across processes.
            await context.Database.ExecuteSqlRawAsync("""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource = N'SmartAttendance.InitialAdmin',
                    @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 10000;
                IF @result < 0 THROW 51010, 'Initial Admin provisioning lock could not be acquired.', 1;
                """, ct);
        }
        if (await context.Users.AnyAsync(u => u.Role == AppRoles.Admin, ct)) return ProvisioningOutcome.AlreadyProvisioned;
        if (await context.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct)) return ProvisioningOutcome.EmailConflict;
        var user = new User { Name = settings.Name.Trim(), Email = settings.Email.Trim(), Role = AppRoles.Admin };
        user.PasswordHash = hasher.HashPassword(user, settings.Password);
        context.Users.Add(user);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ProvisioningOutcome.Created;
    }
}
