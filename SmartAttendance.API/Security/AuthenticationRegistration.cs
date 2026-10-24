using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartAttendance.API.Models;

namespace SmartAttendance.API.Security;

public static class AuthenticationRegistration
{
    public const string LoginRatePolicy = "login";
    public static IServiceCollection AddAttendanceAuthentication(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>().BindConfiguration("Jwt").ValidateDataAnnotations()
            .Validate(JwtOptions.HasSuitableKey, "Jwt:Key must be a non-placeholder, non-repetitive secret of at least 32 UTF-8 bytes. Supply it through User Secrets or environment configuration.")
            .ValidateOnStart();
        services.AddOptions<LoginSecurityOptions>().BindConfiguration("LoginSecurity").ValidateDataAnnotations().ValidateOnStart();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<IOptions<JwtOptions>>((bearer, configured) =>
        {
            var jwt = configured.Value;
            bearer.IncludeErrorDetails = false;
            bearer.SaveToken = false;
            bearer.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
                RequireSignedTokens = true, RequireExpirationTime = true, RequireAudience = true,
                ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ValidTypes = ["JWT"],
                ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds),
                NameClaimType = ClaimTypes.Name, RoleClaimType = ClaimTypes.Role
            };
        });
        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthPolicies.KnownRoles, policy => policy.RequireAuthenticatedUser().RequireRole(AppRoles.Admin, AppRoles.Teacher, AppRoles.Student));
            options.AddPolicy(AuthPolicies.Staff, policy => policy.RequireAuthenticatedUser().RequireRole(AppRoles.Admin, AppRoles.Teacher));
            void Current(string name, params string[] roles) => options.AddPolicy(name, policy => policy.RequireAuthenticatedUser()
                .RequireRole(roles).AddRequirements(new CurrentAccountRequirement(roles)));
            Current(AuthPolicies.Admin, AppRoles.Admin); Current(AuthPolicies.Teacher, AppRoles.Teacher);
            Current(AuthPolicies.Student, AppRoles.Student); Current(AuthPolicies.CurrentStaff, AppRoles.Admin, AppRoles.Teacher);
            Current(AuthPolicies.CurrentAccount, AppRoles.Admin, AppRoles.Teacher, AppRoles.Student);
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireRole(AppRoles.Admin, AppRoles.Teacher, AppRoles.Student).Build();
        });
        services.AddScoped<IAuthorizationHandler, CurrentAccountHandler>();
        services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<LoginAttemptThrottle>();
        services.AddSingleton<DummyPasswordHash>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginRatePolicy, context =>
            {
                var limits = context.RequestServices.GetRequiredService<IOptions<LoginSecurityOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
                    new FixedWindowRateLimiterOptions { PermitLimit = limits.IpPermitLimit, Window = TimeSpan.FromSeconds(limits.IpWindowSeconds), QueueLimit = 0, AutoReplenishment = true });
            });
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                var retry = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay) ? Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds)) : 1;
                context.HttpContext.Response.Headers.RetryAfter = retry.ToString(CultureInfo.InvariantCulture);
                await Results.Problem(statusCode: 429, title: "Too many login attempts. Try again later.").ExecuteAsync(context.HttpContext);
            };
        });
        return services;
    }
}
