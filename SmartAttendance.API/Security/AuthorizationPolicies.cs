using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;

namespace SmartAttendance.API.Security;

public static class AuthPolicies
{
    public const string KnownRoles = "KnownRoles";
    public const string Admin = "AdminAccount";
    public const string Teacher = "TeacherAccount";
    public const string Student = "StudentAccount";
    public const string Staff = "StaffRoles";
    public const string CurrentStaff = "CurrentStaffAccount";
    public const string CurrentAccount = "CurrentAccount";
}

public sealed record CurrentAccountRequirement(string[] AllowedRoles) : IAuthorizationRequirement;

public sealed class CurrentAccountHandler(AppDbContext context) : AuthorizationHandler<CurrentAccountRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext authorization, CurrentAccountRequirement requirement)
    {
        if (!int.TryParse(authorization.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0) return;
        var role = await context.Users.AsNoTracking().Where(u => u.UserId == userId).Select(u => u.Role).SingleOrDefaultAsync();
        if (role is not null && requirement.AllowedRoles.Contains(role, StringComparer.Ordinal) && authorization.User.IsInRole(role))
            authorization.Succeed(requirement);
    }
}
