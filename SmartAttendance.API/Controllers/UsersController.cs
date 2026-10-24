using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;
using SmartAttendance.API.DTOs;
using SmartAttendance.API.Models;
using SmartAttendance.API.Services;

namespace SmartAttendance.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController(
    AppDbContext context, IPasswordHasher<User> passwordHasher, TokenService tokenService) : ControllerBase
{
    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserResponseDto>>> GetUsers(CancellationToken cancellationToken)
    {
        return await context.Users.AsNoTracking().Select(ResponseProjections.User).ToListAsync(cancellationToken);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("{id}")]
    public async Task<ActionResult<UserResponseDto>> GetUser(
        [Range(1, int.MaxValue)] int id, CancellationToken cancellationToken)
    {
        var user = await context.Users.AsNoTracking().Where(u => u.UserId == id)
            .Select(ResponseProjections.User).FirstOrDefaultAsync(cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<UserResponseDto>> CreateUser(RegisterUserDto dto, CancellationToken cancellationToken)
    {
        var email = dto.Email.Trim();
        if (await context.Users.AnyAsync(u => u.Email == email, cancellationToken))
            return Conflict("A user with this email already exists.");

        var user = new User { Name = dto.Name.Trim(), Email = email, Role = dto.Role };
        user.PasswordHash = passwordHasher.HashPassword(user, dto.Password);
        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);
        var response = await context.Users.AsNoTracking().Where(u => u.UserId == user.UserId)
            .Select(ResponseProjections.User).SingleAsync(cancellationToken);
        return CreatedAtAction(nameof(GetUser), new { id = user.UserId }, response);
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponseDto>> Login(LoginDto dto, CancellationToken cancellationToken)
    {
        // Only authentication needs the stored hash; it never enters a response DTO.
        var user = await context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == dto.Email.Trim(), cancellationToken);
        if (user is null)
            return Unauthorized("Invalid email or password.");

        var passwordResult = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password);
        if (passwordResult == PasswordVerificationResult.Failed)
            return Unauthorized("Invalid email or password.");

        return Ok(new LoginResponseDto
        {
            Token = tokenService.CreateToken(user),
            User = new UserResponseDto
            {
                UserId = user.UserId, Name = user.Name, Email = user.Email, Role = user.Role
            }
        });
    }
}
