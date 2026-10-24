using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SmartAttendance.API.Security;

public sealed class AuthenticationOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!context.MethodInfo.IsDefined(typeof(AllowAnonymousAttribute), true)) return;
        // Explicitly override the document's bearer requirement for anonymous login.
        // An empty requirement permits anonymous access. A nonempty array is intentional:
        // this OpenAPI serializer omits [], which would inherit the global bearer requirement.
        operation.Security = [new OpenApiSecurityRequirement()];
        operation.Summary ??= "Sign in with email and password";
        operation.Description = "Anonymous, rate-limited login. Returns the existing token/user DTO. " +
            "Invalid credentials return the same 401 message for unknown and existing accounts. " +
            "429 responses include Retry-After. Passwords, hashes and signing keys are never returned. " +
            "Access tokens expire after the configured lifetime (default 120 minutes); no refresh token is issued.";
    }
}
