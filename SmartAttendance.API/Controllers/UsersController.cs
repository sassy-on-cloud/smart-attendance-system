using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;
using SmartAttendance.API.DTOs;
using SmartAttendance.API.Security;
using SmartAttendance.API.Services;

namespace SmartAttendance.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class UsersController(AppDbContext context, AccountService accounts) : ControllerBase
{
    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserResponseDto>>> GetUsers(CancellationToken cancellationToken) =>
        await context.Users.AsNoTracking().Select(ResponseProjections.User).ToListAsync(cancellationToken);

    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpGet("{id}")]
    public async Task<ActionResult<UserResponseDto>> GetUser([Range(1, int.MaxValue)] int id, CancellationToken cancellationToken)
    {
        var user = await context.Users.AsNoTracking().Where(u => u.UserId == id).Select(ResponseProjections.User).FirstOrDefaultAsync(cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [Authorize(Policy = AuthPolicies.CurrentAccount)]
    [HttpGet("me")]
    public async Task<ActionResult<UserResponseDto>> GetMe(CancellationToken cancellationToken)
    {
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
        return await context.Users.AsNoTracking().Where(u => u.UserId == id).Select(ResponseProjections.User).SingleAsync(cancellationToken);
    }

    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpPost]
    public async Task<ActionResult<UserResponseDto>> CreateUser(RegisterUserDto dto, CancellationToken cancellationToken)
    {
        var result = await accounts.RegisterAsync(dto, cancellationToken);
        return result.Outcome switch
        {
            AccountCreationOutcome.Created => CreatedAtAction(nameof(GetUser), new { id = result.User!.UserId }, result.User),
            AccountCreationOutcome.DuplicateEmail => Conflict("A user with this email already exists."),
            _ => BadRequest("Registration fields do not meet the validation rules.")
        };
    }

    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationRegistration.LoginRatePolicy)]
    [RequestSizeLimit(8192)]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponseDto>> Login(LoginDto dto, CancellationToken cancellationToken)
    {
        var result = await accounts.LoginAsync(dto, cancellationToken);
        if (result.Outcome == LoginOutcome.Throttled)
        {
            Response.Headers.RetryAfter = result.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            return Problem(statusCode: 429, title: "Too many login attempts. Try again later.");
        }
        return result.Outcome == LoginOutcome.Success ? Ok(result.Response) : Unauthorized("Invalid email or password.");
    }
}
