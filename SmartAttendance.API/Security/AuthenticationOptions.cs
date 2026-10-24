using System.ComponentModel.DataAnnotations;
using System.Text;

namespace SmartAttendance.API.Security;

public sealed class JwtOptions
{
    [Required, StringLength(512)]
    public string Key { get; set; } = string.Empty;
    [Required, StringLength(1000)]
    public string Issuer { get; set; } = string.Empty;
    [Required, StringLength(1000)]
    public string Audience { get; set; } = string.Empty;
    [Range(5, 120)]
    public int AccessTokenMinutes { get; set; } = 120;
    [Range(0, 60)]
    public int ClockSkewSeconds { get; set; } = 30;

    public static bool HasSuitableKey(JwtOptions options)
    {
        var key = options.Key;
        return !string.IsNullOrWhiteSpace(key) && Encoding.UTF8.GetByteCount(key) >= 32 &&
            key.Distinct().Count() >= 8 && !new[] { "YOUR_", "REPLACE", "CHANGE", "EXAMPLE", "<", ">" }
                .Any(marker => key.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class LoginSecurityOptions
{
    [Range(1, 1000)] public int IpPermitLimit { get; set; } = 20;
    [Range(1, 3600)] public int IpWindowSeconds { get; set; } = 60;
    [Range(1, 20)] public int AccountAttemptLimit { get; set; } = 5;
    [Range(1, 900)] public int AccountWindowSeconds { get; set; } = 300;
}

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Teacher = "Teacher";
    public const string Student = "Student";
    public static bool IsKnown(string role) => role is Admin or Teacher or Student;
}

public static class EmailIdentity
{
    public static string? Normalize(string email) => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToUpperInvariant();
}

public sealed class PasswordPolicyAttribute : ValidationAttribute
{
    public PasswordPolicyAttribute() => ErrorMessage = "Password must contain 12–128 characters and at least six distinct characters; passphrases are supported.";
    public override bool IsValid(object? value) => value is string password && password.Length is >= 12 and <= 128 &&
        !string.IsNullOrWhiteSpace(password) && password.Distinct().Count() >= 6;
}
