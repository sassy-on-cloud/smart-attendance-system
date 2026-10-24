using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;
using SmartAttendance.API.DTOs;
using SmartAttendance.API.Models;
using SmartAttendance.API.Security;

namespace SmartAttendance.API.Services;

public enum AccountCreationOutcome { Created, DuplicateEmail, Invalid }
public record AccountCreationResult(AccountCreationOutcome Outcome, UserResponseDto? User = null);
public enum LoginOutcome { Success, InvalidCredentials, Throttled }
public record LoginResult(LoginOutcome Outcome, LoginResponseDto? Response = null, int RetryAfterSeconds = 0);

public class AccountService(AppDbContext context, IPasswordHasher<User> hasher, TokenService tokens,
    DummyPasswordHash dummy, LoginAttemptThrottle throttle)
{
    public async Task<AccountCreationResult> RegisterAsync(RegisterUserDto dto, CancellationToken ct)
    {
        if (!Validator.TryValidateObject(dto, new ValidationContext(dto), [], true)) return new(AccountCreationOutcome.Invalid);
        var normalized = EmailIdentity.Normalize(dto.Email);
        if (await context.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct)) return new(AccountCreationOutcome.DuplicateEmail);
        var user = new User { Name = dto.Name.Trim(), Email = dto.Email.Trim(), Role = dto.Role };
        user.PasswordHash = hasher.HashPassword(user, dto.Password);
        context.Users.Add(user);
        try { await context.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            if (await context.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct)) return new(AccountCreationOutcome.DuplicateEmail);
            throw;
        }
        return new(AccountCreationOutcome.Created, new UserResponseDto { UserId = user.UserId, Name = user.Name, Email = user.Email, Role = user.Role });
    }

    public async Task<LoginResult> LoginAsync(LoginDto dto, CancellationToken ct)
    {
        var normalized = EmailIdentity.Normalize(dto.Email);
        if (normalized is null) return new(LoginOutcome.InvalidCredentials);
        var reservation = throttle.Reserve(normalized);
        if (reservation.RetryAfterSeconds > 0) return new(LoginOutcome.Throttled, RetryAfterSeconds: reservation.RetryAfterSeconds);
        var user = await context.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        PasswordVerificationResult verified;
        try { verified = hasher.VerifyHashedPassword(user ?? new User(), user?.PasswordHash ?? dummy.Hash, dto.Password); }
        catch (Exception error) when (error is FormatException or ArgumentException) { verified = PasswordVerificationResult.Failed; }
        if (user is null || user.UserId <= 0 || !AppRoles.IsKnown(user.Role) || verified == PasswordVerificationResult.Failed)
            return new(LoginOutcome.InvalidCredentials);
        if (verified == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, dto.Password);
            await context.SaveChangesAsync(ct);
        }
        throttle.Succeeded(reservation);
        return new(LoginOutcome.Success, new LoginResponseDto
        {
            Token = tokens.CreateToken(user),
            User = new UserResponseDto { UserId = user.UserId, Name = user.Name, Email = user.Email, Role = user.Role }
        });
    }
}
