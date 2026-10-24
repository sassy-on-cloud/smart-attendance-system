using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SmartAttendance.API.Security;

namespace SmartAttendance.API.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class RegisterUserDto
{
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12), PasswordPolicy]
    public string Password { get; set; } = string.Empty;

    [Required, RegularExpression("^(Admin|Teacher|Student)$")]
    public string Role { get; set; } = "Student";
}
