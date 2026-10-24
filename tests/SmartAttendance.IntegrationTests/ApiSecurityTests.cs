using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartAttendance.API.Data;
using SmartAttendance.API.Models;
using Xunit;

namespace SmartAttendance.IntegrationTests;

public sealed class ApiSecurityTests : IClassFixture<SecurityApiFactory>, IDisposable
{
    private readonly SecurityApiFactory _factory;
    private readonly HttpClient _client;

    public ApiSecurityTests(SecurityApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
        _client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    }

    private void Authenticate(string role, string? userId = "101") =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.Token(role, userId));

    private async Task<JsonElement> SafeJson(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(_factory.SigningKey, text, StringComparison.Ordinal);
        Assert.DoesNotContain(SecurityApiFactory.Password, text, StringComparison.Ordinal);
        Assert.DoesNotContain(SecurityApiFactory.FailureSecret, text, StringComparison.Ordinal);
        using var scope = _factory.Services.CreateScope();
        var hashes = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Select(u => u.PasswordHash).ToListAsync();
        foreach (var hash in hashes)
            Assert.DoesNotContain(hash, text, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(text);
        AssertSafeFields(document.RootElement);
        return document.RootElement.Clone();
    }

    private static void AssertSafeFields(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in json.EnumerateObject())
            {
                // ValidationProblemDetails names invalid input fields, without echoing their values.
                if (property.Name == "errors")
                    continue;
                Assert.DoesNotContain(property.Name.ToLowerInvariant(), new[]
                {
                    "password", "passwordhash", "key", "signingkey", "connectionstring", "credentials"
                });
                AssertSafeFields(property.Value);
            }
        }
        else if (json.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in json.EnumerateArray())
                AssertSafeFields(value);
        }
    }

    [Theory]
    [InlineData("/api/users")]
    [InlineData("/api/users/101")]
    [InlineData("/api/students")]
    [InlineData("/api/students/1")]
    [InlineData("/api/courses")]
    [InlineData("/api/courses/1")]
    [InlineData("/api/attendances")]
    [InlineData("/api/attendances/1")]
    public async Task Every_read_endpoint_returns_only_safe_json(string route)
    {
        Authenticate("Admin", "11");
        var response = await _client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await SafeJson(response);
        var expectedFields = route.Split('/')[2] switch
        {
            "users" => new[] { "userId", "name", "email", "role" },
            "students" => new[] { "studentId", "userId", "rollNumber", "name", "email", "department", "semester" },
            "courses" => new[] { "courseId", "courseName", "courseCode" },
            _ => new[] { "attendanceId", "attendanceSessionId", "isLegacy", "studentId", "courseId", "date", "status", "method" }
        };
        var records = json.ValueKind == JsonValueKind.Array ? json.EnumerateArray().ToArray() : [json];
        Assert.NotEmpty(records);
        foreach (var record in records)
            Assert.Equal(expectedFields.Order(), record.EnumerateObject().Select(p => p.Name).Order());
    }

    public static IEnumerable<object[]> RoleMatrix()
    {
        foreach (var role in new[] { "Anonymous", "Admin", "Teacher", "Student", "Unknown" })
        foreach (var resource in new[] { "users", "students", "courses", "attendances" })
        {
            var canRead = role == "Admin" || (role == "Teacher" && resource != "users") ||
                          (role == "Student" && resource is "courses" or "attendances");
            var canWrite = role == "Admin" || (role == "Teacher" && resource is "courses" or "attendances");
            var forbidden = role == "Anonymous" ? 401 : 403;
            yield return [role, $"/api/{resource}", "GET", canRead ? 200 : forbidden];
            yield return [role, $"/api/{resource}/1", "GET", canRead ? (resource == "users" ? 404 : 200) : forbidden];
            // Empty JSON is invalid for an authorized caller; auth must reject others first.
            yield return [role, $"/api/{resource}", "POST", canWrite ? 400 : forbidden];
        }
    }

    [Theory]
    [MemberData(nameof(RoleMatrix))]
    public async Task Existing_roles_are_enforced_through_http(string role, string route, string method, int status)
    {
        if (role != "Anonymous")
            Authenticate(role, role == "Student" ? "101" : role == "Teacher" ? "22" : "11");
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "POST")
            request.Content = JsonContent.Create(new { });
        var response = await _client.SendAsync(request);
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
    }

    [Fact]
    public async Task Students_can_only_list_and_get_their_own_attendance()
    {
        Authenticate("Student", "101");
        var list = await SafeJson(await _client.GetAsync("/api/attendances?studentId=2&userId=202"));
        var own = Assert.Single(list.EnumerateArray());
        Assert.Equal(1, own.GetProperty("studentId").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/attendances/1")).StatusCode);
        var foreign = await _client.GetAsync("/api/attendances/2");
        var missing = await _client.GetAsync("/api/attendances/99999");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(missing.StatusCode, foreign.StatusCode);
        // Test the opposite account as well, not only a conveniently matching seed ID.
        Authenticate("Student", "202");
        var second = await SafeJson(await _client.GetAsync("/api/attendances"));
        Assert.Equal(2, Assert.Single(second.EnumerateArray()).GetProperty("studentId").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/attendances/1")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("99999")]
    [InlineData("303")]
    public async Task Missing_invalid_or_unlinked_account_identity_fails_closed(string? userId)
    {
        Authenticate("Student", userId);
        var response = await _client.GetAsync("/api/attendances");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await SafeJson(response)).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/attendances/1")).StatusCode);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Teacher")]
    public async Task Staff_retains_attendance_read_access(string role)
    {
        Authenticate(role, role == "Teacher" ? "22" : "11");
        Assert.Equal(2, (await SafeJson(await _client.GetAsync("/api/attendances"))).GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/attendances/2")).StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-key")]
    [InlineData("malformed")]
    public async Task Invalid_jwts_are_rejected(string kind)
    {
        var token = kind switch
        {
            "expired" => _factory.Token("Admin", expires: DateTime.UtcNow.AddHours(-1)),
            "wrong-issuer" => _factory.Token("Admin", issuer: "untrusted"),
            "wrong-audience" => _factory.Token("Admin", audience: "untrusted"),
            "wrong-key" => _factory.Token("Admin", key: new string('x', 64)),
            _ => "invalid.jwt.token"
        };
        _client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/attendances")).StatusCode);
    }

    [Fact]
    public async Task Login_returns_a_usable_token_and_safe_user_without_signing_secrets()
    {
        var response = await _client.PostAsJsonAsync("/api/users/login", new { email = "one@example.test", password = SecurityApiFactory.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await SafeJson(response);
        Assert.Equal(new[] { "token", "user" }, json.EnumerateObject().Select(p => p.Name).Order().ToArray());
        var token = json.GetProperty("token").GetString()!;
        // The JWT payload must also contain only approved identity claims.
        var payload = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token).Payload.SerializeToJson();
        Assert.DoesNotContain(_factory.SigningKey, payload);
        Assert.DoesNotContain("PasswordHash", payload, StringComparison.OrdinalIgnoreCase);
        _client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/attendances/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/attendances/2")).StatusCode);
    }

    [Fact]
    public async Task Valid_creates_return_safe_dtos_and_followable_locations()
    {
        Authenticate("Admin", "11");
        var user = await _client.PostAsJsonAsync("/api/users", new { name = "New Student", email = "new@example.test", password = SecurityApiFactory.Password, role = "Student" });
        Assert.Equal(HttpStatusCode.Created, user.StatusCode);
        var userJson = await SafeJson(user);
        var userId = userJson.GetProperty("userId").GetInt32();
        var student = await _client.PostAsJsonAsync("/api/students", new { userId, rollNumber = "CS-03", department = " Computing ", semester = 1 });
        Assert.Equal(HttpStatusCode.Created, student.StatusCode);
        var studentJson = await SafeJson(student);
        Assert.Equal("New Student", studentJson.GetProperty("name").GetString());
        Assert.Equal("new@example.test", studentJson.GetProperty("email").GetString());
        Assert.Equal("Computing", studentJson.GetProperty("department").GetString());
        var course = await _client.PostAsJsonAsync("/api/courses", new { courseName = "Databases", courseCode = "CS-102" });
        Assert.Equal(HttpStatusCode.Created, course.StatusCode);
        var courseJson = await SafeJson(course);
        var courseId = courseJson.GetProperty("courseId").GetInt32();
        var studentId = studentJson.GetProperty("studentId").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"/api/courses/{courseId}/enrollments", new { studentId })).StatusCode);
        var session = await _client.PostAsJsonAsync("/api/attendancesessions", new
        {
            courseId, startsAt = "2026-10-20T09:45:00+05:45", endsAt = "2026-10-20T10:45:00+05:45"
        });
        Assert.Equal(HttpStatusCode.Created, session.StatusCode);
        var sessionJson = await SafeJson(session);
        var attendance = await _client.PostAsJsonAsync("/api/attendances", new
        {
            studentId, attendanceSessionId = sessionJson.GetProperty("attendanceSessionId").GetInt32(), status = "Present"
        });
        Assert.Equal(HttpStatusCode.Created, attendance.StatusCode);
        var attendanceJson = await SafeJson(attendance);
        Assert.Equal("Manual", attendanceJson.GetProperty("method").GetString());
        Assert.True(attendanceJson.GetProperty("date").GetDateTime() > DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(new[] { "attendanceId", "attendanceSessionId", "courseId", "date", "isLegacy", "method", "status", "studentId" },
            attendanceJson.EnumerateObject().Select(p => p.Name).Order().ToArray());
        foreach (var created in new[] { user, student, course, session, attendance })
        {
            Assert.NotNull(created.Headers.Location);
            var read = await _client.GetAsync(created.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal((await created.Content.ReadAsStringAsync()), (await read.Content.ReadAsStringAsync()));
        }
    }

    [Fact]
    public async Task Teacher_can_still_create_courses_and_manual_attendance()
    {
        Authenticate("Teacher", "22");
        var course = await _client.PostAsJsonAsync("/api/courses", new { courseName = "Networks", courseCode = "CS-103" });
        Assert.Equal(HttpStatusCode.Created, course.StatusCode);
        await SafeJson(course);
        var attendance = await _client.PostAsJsonAsync("/api/attendances", new
        {
            studentId = 1, attendanceSessionId = 2, status = "Late"
        });
        Assert.Equal(HttpStatusCode.Created, attendance.StatusCode);
        Assert.Equal("Manual", (await SafeJson(attendance)).GetProperty("method").GetString());
    }

    [Fact]
    public async Task Incorrect_and_missing_login_accounts_have_the_same_safe_failure()
    {
        var incorrect = await _client.PostAsJsonAsync("/api/users/login", new { email = "one@example.test", password = "incorrect-password" });
        var missing = await _client.PostAsJsonAsync("/api/users/login", new { email = "missing@example.test", password = "incorrect-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, incorrect.StatusCode);
        Assert.Equal(incorrect.StatusCode, missing.StatusCode);
        Assert.Equal(await incorrect.Content.ReadAsStringAsync(), await missing.Content.ReadAsStringAsync());
        Assert.Equal("Invalid email or password.", await incorrect.Content.ReadAsStringAsync());
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        yield return ["users", JsonSerializer.Serialize(new { name = new string('a', 101), email = "valid@example.test", password = "valid-password" })];
        yield return ["users", JsonSerializer.Serialize(new { name = "Valid", email = "valid@example.test", password = new string('a', 129) })];
        yield return ["students", JsonSerializer.Serialize(new { userId = 303, rollNumber = new string('a', 51), department = "Computing", semester = 1 })];
        yield return ["students", JsonSerializer.Serialize(new { userId = 303, rollNumber = "CS-03", department = new string('a', 101), semester = 1 })];
        yield return ["courses", JsonSerializer.Serialize(new { courseName = new string('a', 201), courseCode = "CS-103" })];
        yield return ["courses", JsonSerializer.Serialize(new { courseName = "Course", courseCode = new string('a', 31) })];
        yield return ["users", "{}"];
        yield return ["users", """{"name":"  ","email":"bad","password":"short","role":"Root"}"""];
        yield return ["users", """{"name":"Valid","email":"valid@example.test","password":"short"}"""];
        yield return ["users", """{"name":"Valid","email":"valid@example.test","password":"valid-password","role":"Root"}"""];
        yield return ["students", "{}"];
        yield return ["students", """{"userId":0,"rollNumber":"CS-03","department":"Computing","semester":1}"""];
        yield return ["students", """{"userId":303,"rollNumber":"invalid space","department":"Computing","semester":1}"""];
        yield return ["students", """{"userId":303,"rollNumber":"CS-03","department":"  ","semester":1}"""];
        yield return ["students", """{"userId":303,"rollNumber":"CS-03","department":"Computing","semester":0}"""];
        yield return ["courses", "{}"];
        yield return ["courses", """{"courseName":"  ","courseCode":"CS101"}"""];
        yield return ["courses", """{"courseName":"Course","courseCode":"bad space"}"""];
        yield return ["attendances", "{}"];
        yield return ["attendances", """{"studentId":0,"attendanceSessionId":2,"status":"Present"}"""];
        yield return ["attendances", """{"studentId":1,"attendanceSessionId":-1,"status":"Present"}"""];
        yield return ["attendances", """{"studentId":1,"status":"Present"}"""];
        yield return ["attendances", """{"studentId":1,"attendanceSessionId":"bad","status":"Present"}"""];
        yield return ["attendances", """{"studentId":1,"attendanceSessionId":2,"status":"Invented"}"""];
        yield return ["users/login", "{}"];
        yield return ["users/login", """{"email":"bad","password":"test"}"""];
        yield return ["users/login", """{"email":"one@example.test","password":null}"""];
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Invalid_requests_are_rejected_before_any_write(string resource, string body)
    {
        Authenticate("Admin", "11");
        var before = await Counts();
        var response = await _client.PostAsync($"/api/{resource}", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await SafeJson(response);
        Assert.Equal(before, await Counts());
    }

    public static IEnumerable<object[]> OverpostingRequests()
    {
        var requests = new Dictionary<string, string>
        {
            ["users/login"] = """{"email":"one@example.test","password":"valid-password"}""",
            ["users"] = """{"name":"Injected","email":"injected@example.test","password":"valid-password","role":"Student"}""",
            ["students"] = """{"userId":303,"rollNumber":"CS-03","department":"Computing","semester":1}""",
            ["courses"] = """{"courseName":"Injected","courseCode":"CS999"}""",
            ["attendances"] = """{"studentId":1,"attendanceSessionId":2,"status":"Present"}"""
        };
        foreach (var (resource, body) in requests)
        {
            var fields = resource switch
            {
                "users/login" => new[] { "role", "userId", "passwordHash" },
                "users" => new[] { "userId", "passwordHash", "jwt" },
                "students" => new[] { "studentId", "user", "role", "passwordHash" },
                "courses" => new[] { "courseId", "attendances" },
                _ => new[] { "attendanceId", "student", "course", "method", "recordedBy", "courseId", "date", "legacyStatus", "legacyMethod" }
            };
            foreach (var field in fields)
            {
                var json = JsonSerializer.Deserialize<Dictionary<string, object>>(body)!;
                json[field] = field is "user" or "student" or "course"
                    ? new { user = new { role = "Admin", passwordHash = "injected-hash" }, role = "Admin" }
                    : (object)"injected";
                yield return [resource, JsonSerializer.Serialize(json)];
            }
        }
    }

    [Theory]
    [MemberData(nameof(OverpostingRequests))]
    public async Task Server_fields_and_nested_graphs_are_rejected_without_side_effects(string resource, string body)
    {
        Authenticate(resource == "attendances" ? "Teacher" : "Admin", "11");
        var before = await Counts();
        var response = await _client.PostAsync($"/api/{resource}", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await Counts());
        using var scope = _factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.FindAsync(303);
        Assert.Equal("Student", user!.Role);
        Assert.NotEqual("injected-hash", user.PasswordHash);
    }

    [Theory]
    [InlineData("users")]
    [InlineData("students")]
    [InlineData("courses")]
    [InlineData("attendances")]
    public async Task Invalid_route_ids_are_validation_errors(string resource)
    {
        Authenticate("Admin", "11");
        foreach (var id in new[] { "0", "-1", "invalid", "2147483648" })
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync($"/api/{resource}/{id}")).StatusCode);
    }

    [Theory]
    [InlineData(99999, 400)]
    [InlineData(11, 400)]
    [InlineData(22, 400)]
    [InlineData(101, 409)]
    public async Task Student_requires_existing_student_account_without_existing_profile(int userId, int status)
    {
        Authenticate("Admin", "11");
        var response = await _client.PostAsJsonAsync("/api/students", new { userId, rollNumber = "CS-03", department = "Computing", semester = 1 });
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
    }

    [Theory]
    [InlineData(99999, 1)]
    [InlineData(1, 99999)]
    public async Task Attendance_requires_existing_foreign_keys(int studentId, int attendanceSessionId)
    {
        Authenticate("Teacher", "22");
        var response = await _client.PostAsJsonAsync("/api/attendances", new { studentId, attendanceSessionId, status = "Present" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal((5, 2, 1, 2), await Counts());
    }

    [Fact]
    public async Task Unexpected_errors_do_not_disclose_exception_secrets_even_in_development()
    {
        // A host override exercises the exception handler in Development as well.
        using var development = new SecurityApiFactory { EnvironmentName = "Development" };
        development.ResetDatabase();
        using var client = development.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new("Bearer", development.Token("Admin", "11"));
        development.Probe.ThrowOnRead = true;
        try
        {
            var response = await client.GetAsync("/api/users");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(SecurityApiFactory.FailureSecret, body);
            Assert.DoesNotContain(development.SigningKey, body);
            Assert.DoesNotContain("InvalidOperationException", body);
            Assert.Equal("An unexpected error occurred.", JsonDocument.Parse(body).RootElement.GetProperty("title").GetString());
        }
        finally { development.Probe.ThrowOnRead = false; }
    }

    [Fact]
    public void Password_hash_has_a_serialization_safety_net()
    {
        var json = JsonSerializer.Serialize(new User { PasswordHash = "never-serialize-this" });
        Assert.DoesNotContain("PasswordHash", json);
        Assert.DoesNotContain("never-serialize-this", json);
    }

    private async Task<(int Users, int Students, int Courses, int Attendances)> Counts()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Users.CountAsync(), await db.Students.CountAsync(), await db.Courses.CountAsync(), await db.Attendances.CountAsync());
    }

    public void Dispose() => _client.Dispose();
}
