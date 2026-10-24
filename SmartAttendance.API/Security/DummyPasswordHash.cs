using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SmartAttendance.API.Models;

namespace SmartAttendance.API.Security;

public sealed class DummyPasswordHash(IOptions<PasswordHasherOptions> options)
{
    // Work factor for unknown identities; this random value is never a usable account credential.
    public string Hash { get; } = new PasswordHasher<User>(options).HashPassword(new User(), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
}
