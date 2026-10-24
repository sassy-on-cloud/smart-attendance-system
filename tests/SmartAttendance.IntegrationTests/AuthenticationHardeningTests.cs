using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartAttendance.API.Data;
using SmartAttendance.API.Models;
using SmartAttendance.API.Security;
using Xunit;

namespace SmartAttendance.IntegrationTests;

public sealed class AuthenticationHardeningTests : IDisposable
{
    private readonly SecurityApiFactory _factory = new();
    private readonly HttpClient _client;
    public AuthenticationHardeningTests()
    {
        _factory.ResetDatabase(); _client = _factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
    }
    private void Authenticate(string role = "Admin", string id = "11") => _client.DefaultRequestHeaders.Authorization = new("Bearer", _factory.Token(role, id));
    private Task<HttpResponseMessage> Login(string email = "one@example.test", string password = SecurityApiFactory.Password) =>
        _client.PostAsJsonAsync("/api/users/login", new { email, password });
    private Task<HttpResponseMessage> Register(string email = "new@example.test", string role = "Student", string password = SecurityApiFactory.Password) =>
        _client.PostAsJsonAsync("/api/users", new { name = "New User", email, role, password });

    [Fact]
    public async Task Valid_login_normalizes_email_rehashes_legacy_password_and_returns_safe_usable_token()
    {
        string before;
        using (var scope = _factory.Services.CreateScope()) before = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.FindAsync(101))!.PasswordHash;
        var response = await Login("ONE@EXAMPLE.TEST"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "token", "user" }, json.EnumerateObject().Select(p => p.Name).Order().ToArray());
        var token = json.GetProperty("token").GetString()!;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.Header.Alg);
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Jti);
        Assert.InRange((jwt.ValidTo - jwt.ValidFrom).TotalMinutes, 119.9, 120.1);
        Assert.DoesNotContain(_factory.SigningKey, await response.Content.ReadAsStringAsync());
        using (var scope = _factory.Services.CreateScope())
        {
            var user = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.FindAsync(101))!;
            Assert.NotEqual(before, user.PasswordHash);
            Assert.Equal(PasswordVerificationResult.Success, scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>().VerifyHashedPassword(user, user.PasswordHash, SecurityApiFactory.Password));
        }
        _client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var me = await _client.GetFromJsonAsync<JsonElement>("/api/users/me"); Assert.Equal(101, me.GetProperty("userId").GetInt32());
        Assert.False(me.TryGetProperty("passwordHash", out _)); Assert.False(me.TryGetProperty("normalizedEmail", out _));
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/attendances/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/attendances/2")).StatusCode);
        Assert.Equal("closed-past-v1", (await _client.GetFromJsonAsync<JsonElement>("/api/reports/policy")).GetProperty("version").GetString());
    }

    [Theory]
    [InlineData("one@example.test", "incorrect")]
    [InlineData("missing@example.test", "incorrect")]
    public async Task Invalid_credentials_have_the_same_401_without_secrets(string email, string password)
    {
        var response = await Login(email, password); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Invalid email or password.", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("malformed-hash")]
    [InlineData("unknown-role")]
    public async Task Corrupt_hashes_or_unsupported_roles_cannot_issue_tokens(string scenario)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var user = (await db.Users.FindAsync(101))!;
            if (scenario == "malformed-hash") user.PasswordHash = "invalid-base64"; else user.Role = "Root";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login()).StatusCode);
    }

    [Theory]
    [InlineData("Anonymous", "", 401)]
    [InlineData("Student", "101", 403)]
    [InlineData("Teacher", "22", 403)]
    [InlineData("Unknown", "11", 403)]
    [InlineData("Admin", "101", 403)]
    public async Task Unauthorized_role_assignment_is_rejected(string role, string id, int status)
    {
        if (role != "Anonymous") Authenticate(role, id);
        Assert.Equal((HttpStatusCode)status, (await Register(role: "Admin")).StatusCode);
        using var scope = _factory.Services.CreateScope(); Assert.Equal(5, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync());
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Teacher")]
    [InlineData("Student")]
    public async Task Current_admin_can_register_recognized_roles_with_hashed_passwords(string role)
    {
        Authenticate(); var response = await Register(role: role); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(role, json.GetProperty("role").GetString());
        Assert.False(json.TryGetProperty("passwordHash", out _));
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var created = await db.Users.SingleAsync(u => u.Email == "new@example.test");
        Assert.Equal("NEW@EXAMPLE.TEST", created.NormalizedEmail);
        Assert.NotEqual(SecurityApiFactory.Password, created.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success, scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>().VerifyHashedPassword(created, created.PasswordHash, SecurityApiFactory.Password));
    }

    [Theory]
    [InlineData("ONE@example.test")]
    [InlineData(" one@example.test ")]
    public async Task Duplicate_email_comparison_is_case_insensitive_and_trimmed(string email)
    {
        Authenticate(); Assert.Equal(HttpStatusCode.Conflict, (await Register(email)).StatusCode);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaaaaaa")]
    [InlineData("            ")]
    public async Task Weak_new_passwords_are_rejected(string password)
    {
        Authenticate(); Assert.Equal(HttpStatusCode.BadRequest, (await Register(password: password)).StatusCode);
    }

    [Fact]
    public async Task Role_escalation_strings_and_server_fields_are_rejected()
    {
        Authenticate(); Assert.Equal(HttpStatusCode.BadRequest, (await Register(role: "Student,Admin")).StatusCode);
        var response = await _client.PostAsJsonAsync("/api/users", new { name = "Injected", email = "injected@example.test", password = SecurityApiFactory.Password,
            role = "Student", normalizedEmail = "OTHER@EXAMPLE.TEST", passwordHash = "injected" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Stale_admin_or_teacher_claims_do_not_keep_management_or_private_directory_access()
    {
        Authenticate();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); (await db.Users.FindAsync(11))!.Role = "Student";
            (await db.Users.FindAsync(22))!.Role = "Student"; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Register(role: "Admin")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/reports/admin/dashboard")).StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<JsonElement[]>("/api/attendances"))!);
        Authenticate("Teacher", "22");
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/students")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.PostAsJsonAsync("/api/courses", new { courseCode = "C6", courseName = "Course" })).StatusCode);
        Assert.Empty((await _client.GetFromJsonAsync<JsonElement[]>("/api/attendancesessions"))!);
    }

    [Theory]
    [InlineData("unsigned")]
    [InlineData("wrong-algorithm")]
    [InlineData("missing-expiration")]
    [InlineData("future-not-before")]
    public async Task Invalid_token_forms_return_401_without_validation_details(string scenario)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "11"), new Claim(ClaimTypes.Role, "Admin") };
        var credentials = scenario == "unsigned" ? null : new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_factory.SigningKey)),
            scenario == "wrong-algorithm" ? SecurityAlgorithms.HmacSha384 : SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(SecurityApiFactory.Issuer, SecurityApiFactory.Audience, claims,
            notBefore: scenario == "future-not-before" ? DateTime.UtcNow.AddMinutes(5) : DateTime.UtcNow.AddMinutes(-1),
            expires: scenario == "missing-expiration" ? null : DateTime.UtcNow.AddMinutes(10), signingCredentials: credentials);
        _client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        var response = await _client.GetAsync("/api/users"); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("error_description", response.Headers.WwwAuthenticate.ToString());
    }

    [Theory]
    [InlineData(AuthPolicies.Admin, "Admin", "11", true)]
    [InlineData(AuthPolicies.Teacher, "Teacher", "22", true)]
    [InlineData(AuthPolicies.Student, "Student", "101", true)]
    [InlineData(AuthPolicies.Admin, "Admin", "101", false)]
    [InlineData(AuthPolicies.Student, "Teacher", "101", false)]
    public async Task Named_policies_match_current_account_roles(string policy, string role, string id, bool allowed)
    {
        using var scope = _factory.Services.CreateScope();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Role, role)], "test"));
        Assert.Equal(allowed, (await scope.ServiceProvider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(principal, null, policy)).Succeeded);
    }

    [Fact]
    public async Task Teacher_directory_is_limited_to_assigned_course_rosters_even_with_extra_admin_claims()
    {
        int hiddenStudent;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hidden = new Student { UserId = 303, RollNumber = "R3", Department = "Computing", Semester = 1 };
            db.Students.Add(hidden);
            db.Users.Add(new User { UserId = 23, Name = "Unassigned Teacher", Email = "unassigned@example.test", Role = "Teacher" });
            await db.SaveChangesAsync(); hiddenStudent = hidden.StudentId;
        }
        var token = new JwtSecurityToken(SecurityApiFactory.Issuer, SecurityApiFactory.Audience,
            [new Claim(ClaimTypes.NameIdentifier, "22"), new Claim(ClaimTypes.Role, "Teacher"), new Claim(ClaimTypes.Role, "Admin")],
            expires: DateTime.UtcNow.AddMinutes(10), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_factory.SigningKey)), SecurityAlgorithms.HmacSha256));
        _client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        Assert.Equal(2, (await _client.GetFromJsonAsync<JsonElement[]>("/api/students"))!.Length);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/students/{hiddenStudent}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/users")).StatusCode);
        Authenticate("Teacher", "23");
        Assert.Empty((await _client.GetFromJsonAsync<JsonElement[]>("/api/students"))!);
    }

    [Fact]
    public async Task Swagger_marks_login_as_anonymous_and_documents_throttling_without_secrets()
    {
        using var host = new SecurityApiFactory { EnvironmentName = "Development" };
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var swagger = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var login = swagger.GetProperty("paths").GetProperty("/api/Users/login").GetProperty("post");
        var anonymous = Assert.Single(login.GetProperty("security").EnumerateArray());
        Assert.Empty(anonymous.EnumerateObject());
        Assert.True(login.GetProperty("responses").TryGetProperty("429", out _));
        Assert.DoesNotContain(host.SigningKey, swagger.GetRawText());
    }

    public void Dispose() { _client.Dispose(); _factory.Dispose(); }
}
