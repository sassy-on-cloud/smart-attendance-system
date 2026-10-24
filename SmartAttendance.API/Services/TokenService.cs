using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartAttendance.API.Models;
using SmartAttendance.API.Security;

namespace SmartAttendance.API.Services;

public class TokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    public string CreateToken(User user)
    {
        if (user.UserId <= 0 || !AppRoles.IsKnown(user.Role))
            throw new ArgumentException("A recognized persisted account is required for token issuance.");
        var jwt = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, user.Name), new Claim(ClaimTypes.Email, user.Email), new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(now).ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)
        };
        var token = new JwtSecurityToken(jwt.Issuer, jwt.Audience, claims, now, now.AddMinutes(jwt.AccessTokenMinutes),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
