using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartAttendance.API.Data;
using SmartAttendance.API.Models;
using SmartAttendance.API.Recognition;
using Xunit;

namespace SmartAttendance.IntegrationTests;

public sealed class RecognitionContractTests : IDisposable
{
    private readonly MockRecognitionComponent _mock = new();
    private readonly SecurityApiFactory _factory;
    private readonly HttpClient _client;
    private readonly RecognitionClock _clock = new();
    private readonly Guid _capture = Guid.NewGuid();
    private const string RecognizePath = "/api/recognition/sessions/2/recognize";
    private const string EnrollPath = "/api/recognition/students/1/enrollment";

    public RecognitionContractTests()
    {
        _factory = new() { RecognitionComponent = _mock, TestClock = _clock };
        _factory.ResetDatabase();
        _client = _factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Authenticate("Teacher", "22");
        Edit(db =>
        {
            var s = db.AttendanceSessions.Find(2)!;
            s.StartsAtUtc = _clock.GetUtcNow().UtcDateTime.AddMinutes(-5);
            s.EndsAtUtc = _clock.GetUtcNow().UtcDateTime.AddMinutes(55);
        });
    }
    private void Authenticate(string role, string? id) =>
        _client.DefaultRequestHeaders.Authorization = new("Bearer", _factory.Token(role, id));
    private Task<HttpResponseMessage> Post(string path = RecognizePath) =>
        _client.PostAsJsonAsync(path, new { captureId = _capture });
    private void Edit(Action<AppDbContext> action)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); action(db); db.SaveChanges();
    }
    private void AssertNoWrites()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, db.Attendances.Count());
        Assert.Empty(db.AttendanceHistory);
    }
    private async Task<JsonElement> AssertOutcome(HttpResponseMessage response, int status, string outcome)
    {
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(outcome, body.GetProperty("outcome").GetString());
        Assert.NotEqual(Guid.Empty, body.GetProperty("requestId").GetGuid());
        Assert.DoesNotContain("passwordHash", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_factory.SigningKey, body.GetRawText());
        AssertNoWrites(); return body;
    }

    [Theory]
    [InlineData("Admin", "11")]
    [InlineData("Teacher", "22")]
    public async Task Simulated_match_is_bound_to_server_identity_and_session_and_never_marks_attendance(string role, string id)
    {
        Authenticate(role, id);
        var response = await Post(); var body = await AssertOutcome(response, 200, "SimulatedMatch");
        Assert.Equal(1, body.GetProperty("studentId").GetInt32());
        Assert.Equal(.9, body.GetProperty("confidence").GetDouble());
        Assert.True(body.GetProperty("isSimulation").GetBoolean());
        Assert.False(body.GetProperty("attendanceRecorded").GetBoolean());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var c = _mock.LastRecognition!;
        Assert.Equal(int.Parse(id), c.ActorUserId); Assert.Equal(2, c.AttendanceSessionId);
        Assert.Equal(1, c.CourseId); Assert.Equal(1, c.SessionVersion);
        Assert.Equal(_capture, c.CaptureId); Assert.Equal(body.GetProperty("requestId").GetGuid(), c.RequestId);
        Assert.Equal(RecognitionWorkflow.ContractVersion, c.ContractVersion);
    }

    [Fact]
    public async Task Mock_enrollment_has_server_target_and_is_explicitly_not_persisted()
    {
        Authenticate("Admin", "11");
        var body = await AssertOutcome(await Post(EnrollPath), 200, "SimulatedEnrollment");
        Assert.True(body.GetProperty("isSimulation").GetBoolean());
        Assert.False(body.GetProperty("enrollmentStored").GetBoolean());
        Assert.Equal(1, _mock.LastEnrollment!.StudentId); Assert.Equal(11, _mock.LastEnrollment.ActorUserId);
    }

    [Theory]
    [InlineData("Anonymous", null, 401, 401)]
    [InlineData("Student", "101", 403, 403)]
    [InlineData("Teacher", "22", 200, 403)]
    [InlineData("Unknown", "11", 403, 403)]
    [InlineData("Admin", "101", 403, 403)]
    [InlineData("Admin", "999", 403, 403)]
    [InlineData("Teacher", null, 403, 403)]
    public async Task Authorization_matrix(string role, string? id, int recognition, int enrollment)
    {
        _client.DefaultRequestHeaders.Authorization = null;
        if (role != "Anonymous") Authenticate(role, id);
        Assert.Equal((HttpStatusCode)recognition, (await Post()).StatusCode);
        Assert.Equal((HttpStatusCode)enrollment, (await Post(EnrollPath)).StatusCode);
        Assert.Equal(recognition == 200 ? 1 : 0, _mock.RecognitionCalls); Assert.Equal(0, _mock.EnrollmentCalls);
        AssertNoWrites();
    }

    [Fact]
    public async Task Unassigned_teacher_is_denied_before_component_call()
    {
        Edit(db => db.Users.Add(new User { UserId = 23, Role = "Teacher" }));
        Authenticate("Teacher", "23");
        await AssertOutcome(await Post(), 403, "Forbidden"); Assert.Equal(0, _mock.RecognitionCalls);
    }

    [Theory]
    [InlineData("studentId", "1")]
    [InlineData("confidence", "1.0")]
    [InlineData("outcome", "\"Matched\"")]
    [InlineData("method", "\"FaceRecognition\"")]
    [InlineData("actorUserId", "11")]
    [InlineData("isSimulation", "false")]
    [InlineData("componentResult", "{}")]
    public async Task Browser_proof_and_server_fields_are_rejected_on_both_routes(string field, string value)
    {
        Authenticate("Admin", "11");
        var body = $"{{\"captureId\":\"{_capture}\",\"{field}\":{value}}}";
        foreach (var path in new[] { RecognizePath, EnrollPath })
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(0, _mock.RecognitionCalls); Assert.Equal(0, _mock.EnrollmentCalls); AssertNoWrites();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"captureId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"captureId\":\"not-a-capture\"}")]
    public async Task Invalid_capture_handles_are_400(string body)
    {
        Authenticate("Admin", "11");
        foreach (var path in new[] { RecognizePath, EnrollPath })
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(0, _mock.RecognitionCalls); Assert.Equal(0, _mock.EnrollmentCalls);
    }

    [Theory]
    [InlineData(ComponentOutcome.NoFace, 422, "NoFace")]
    [InlineData(ComponentOutcome.UnknownFace, 422, "UnknownFace")]
    [InlineData(ComponentOutcome.MultipleFaces, 422, "MultipleFaces")]
    [InlineData(ComponentOutcome.AmbiguousMatch, 422, "AmbiguousMatch")]
    [InlineData(ComponentOutcome.InvalidCapture, 400, "InvalidCapture")]
    [InlineData(ComponentOutcome.Unavailable, 503, "ServiceUnavailable")]
    [InlineData(ComponentOutcome.Enrolled, 502, "InvalidComponentResponse")]
    public async Task Nonmatch_outcomes_do_not_disclose_identity_or_write(ComponentOutcome outcome, int status, string code)
    {
        _mock.Recognize = (c, _) => Task.FromResult(MockRecognitionComponent.Match(c, null, null, outcome));
        var body = await AssertOutcome(await Post(), status, code);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("studentId").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("confidence").ValueKind);
    }

    [Theory]
    [InlineData("Closed")]
    [InlineData("Cancelled")]
    [InlineData("Legacy")]
    [InlineData("Future")]
    [InlineData("Ended")]
    public async Task Only_actual_current_open_sessions_can_invoke_component(string kind)
    {
        Edit(db =>
        {
            var s = db.AttendanceSessions.Find(2)!;
            switch (kind)
            {
                case "Legacy": s.IsLegacy = true; s.LegacyAttendanceId = 222; s.StartsAtUtc = null; s.EndsAtUtc = null; break;
                case "Future": s.StartsAtUtc = _clock.GetUtcNow().UtcDateTime.AddMinutes(1); break;
                case "Ended": s.EndsAtUtc = _clock.GetUtcNow().UtcDateTime; break;
                default: s.State = kind; break;
            }
        });
        await AssertOutcome(await Post(), 409, "SessionNotActive"); Assert.Equal(0, _mock.RecognitionCalls);
    }

    [Theory]
    [InlineData("request")]
    [InlineData("capture")]
    [InlineData("actor")]
    [InlineData("session")]
    [InlineData("version")]
    [InlineData("protocol")]
    [InlineData("past")]
    [InlineData("future")]
    [InlineData("confidence")]
    [InlineData("nan")]
    [InlineData("missingIdentity")]
    [InlineData("unexpectedIdentity")]
    public async Task Unbound_or_malformed_component_responses_fail_closed(string kind)
    {
        _mock.Recognize = (c, _) => Task.FromResult(kind switch
        {
            "request" => MockRecognitionComponent.Match(c) with { RequestId = Guid.NewGuid() },
            "capture" => MockRecognitionComponent.Match(c) with { CaptureId = Guid.NewGuid() },
            "actor" => MockRecognitionComponent.Match(c) with { ActorUserId = 11 },
            "session" => MockRecognitionComponent.Match(c) with { AttendanceSessionId = 1 },
            "version" => MockRecognitionComponent.Match(c) with { SessionVersion = 99 },
            "protocol" => MockRecognitionComponent.Match(c) with { ContractVersion = "unsupported" },
            "past" => MockRecognitionComponent.Match(c) with { CompletedAtUtc = c.IssuedAtUtc.AddSeconds(-1) },
            "future" => MockRecognitionComponent.Match(c) with { CompletedAtUtc = c.IssuedAtUtc.AddSeconds(1) },
            "confidence" => MockRecognitionComponent.Match(c, confidence: 1.1),
            "nan" => MockRecognitionComponent.Match(c, confidence: double.NaN),
            "missingIdentity" => MockRecognitionComponent.Match(c, null),
            _ => MockRecognitionComponent.Match(c, outcome: ComponentOutcome.UnknownFace)
        });
        await AssertOutcome(await Post(), 502, "InvalidComponentResponse");
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("time")]
    [InlineData("assignment")]
    [InlineData("role")]
    public async Task Security_and_session_changes_during_component_call_are_rechecked(string kind)
    {
        _mock.Recognize = (c, _) =>
        {
            Edit(db =>
            {
                if (kind == "cancel") { var s = db.AttendanceSessions.Find(2)!; s.State = "Cancelled"; s.Version++; }
                if (kind == "assignment") db.TeacherCourseAssignments.Remove(db.TeacherCourseAssignments.Find(22, 1)!);
                if (kind == "role") db.Users.Find(22)!.Role = "Student";
            });
            if (kind == "time") _clock.Now = _clock.Now.AddHours(1);
            return Task.FromResult(MockRecognitionComponent.Match(c) with { CompletedAtUtc = _clock.GetUtcNow() });
        };
        await AssertOutcome(await Post(), kind is "role" or "assignment" ? 403 : kind == "time" ? 502 : 409,
            kind is "role" or "assignment" ? "Forbidden" : kind == "time" ? "InvalidComponentResponse" : "SessionChanged");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("role")]
    [InlineData("inferred")]
    [InlineData("unenrolled")]
    public async Task Matched_identity_requires_current_student_and_confirmed_enrollment(string kind)
    {
        Edit(db =>
        {
            if (kind == "role") db.Users.Find(101)!.Role = "Teacher";
            if (kind == "inferred") db.CourseEnrollments.Find(1, 1)!.IsLegacyInferred = true;
            if (kind == "unenrolled") db.Students.Add(new Student { StudentId = 3, UserId = 303, RollNumber = "CS-03" });
        });
        _mock.Recognize = (c, _) => Task.FromResult(MockRecognitionComponent.Match(c, kind == "missing" ? 999 : kind == "unenrolled" ? 3 : 1));
        var body = await AssertOutcome(await Post(), 422, "IneligibleMatch");
        Assert.Equal(JsonValueKind.Null, body.GetProperty("studentId").ValueKind);
    }

    [Fact]
    public async Task Duplicate_existing_attendance_is_409_and_is_never_overwritten()
    {
        Edit(db => db.Attendances.Add(new Attendance { AttendanceId = 3, StudentId = 1, CourseId = 1, AttendanceSessionId = 2, Method = "Manual", Status = "Absent" }));
        var response = await Post(); Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("DuplicateAttendance", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("outcome").GetString());
        using var scope = _factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(3, db.Attendances.Count()); Assert.Equal("Absent", db.Attendances.Find(3)!.Status); Assert.Empty(db.AttendanceHistory);
    }

    [Fact]
    public async Task Nullable_confidence_and_mock_replay_remain_explicit_nonwriting_previews()
    {
        _mock.Recognize = (c, _) => Task.FromResult(MockRecognitionComponent.Match(c, confidence: null));
        var first = await AssertOutcome(await Post(), 200, "SimulatedMatch");
        var second = await AssertOutcome(await Post(), 200, "SimulatedMatch");
        Assert.NotEqual(first.GetProperty("requestId").GetGuid(), second.GetProperty("requestId").GetGuid());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("confidence").ValueKind);
    }

    [Fact]
    public async Task Replayed_component_response_is_rejected_for_new_request()
    {
        RecognitionComponentResult? saved = null;
        _mock.Recognize = (c, _) => Task.FromResult(saved ??= MockRecognitionComponent.Match(c));
        await AssertOutcome(await Post(), 200, "SimulatedMatch");
        await AssertOutcome(await Post(), 502, "InvalidComponentResponse");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unapproved_real_results_cannot_activate_enrollment_or_attendance(bool enrollment)
    {
        Authenticate("Admin", "11");
        _mock.Enroll = (c, _) => Task.FromResult(MockRecognitionComponent.Enrollment(c) with { IsSimulation = false });
        _mock.Recognize = (c, _) => Task.FromResult(MockRecognitionComponent.Match(c) with { IsSimulation = false });
        await AssertOutcome(await Post(enrollment ? EnrollPath : RecognizePath), 503, "IntegrationNotApproved");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Component_exceptions_are_sanitized(bool enrollment)
    {
        Authenticate("Admin", "11");
        _mock.Enroll = (_, _) => throw new InvalidOperationException(SecurityApiFactory.FailureSecret);
        _mock.Recognize = (_, _) => throw new InvalidOperationException(SecurityApiFactory.FailureSecret);
        var response = await Post(enrollment ? EnrollPath : RecognizePath);
        var body = await AssertOutcome(response, 503, "ServiceUnavailable");
        Assert.DoesNotContain(SecurityApiFactory.FailureSecret, body.GetRawText());
    }

    [Theory]
    [InlineData(ComponentOutcome.MultipleFaces, 422, "MultipleFaces")]
    [InlineData(ComponentOutcome.NoFace, 422, "NoFace")]
    [InlineData(ComponentOutcome.InvalidCapture, 400, "InvalidCapture")]
    [InlineData(ComponentOutcome.AlreadyEnrolled, 409, "AlreadyEnrolled")]
    [InlineData(ComponentOutcome.Unavailable, 503, "ServiceUnavailable")]
    [InlineData(ComponentOutcome.Matched, 502, "InvalidComponentResponse")]
    public async Task Enrollment_errors_have_explicit_outcomes(ComponentOutcome outcome, int status, string code)
    {
        Authenticate("Admin", "11");
        _mock.Enroll = (c, _) => Task.FromResult(MockRecognitionComponent.Enrollment(c) with { Outcome = outcome });
        await AssertOutcome(await Post(EnrollPath), status, code);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("request")]
    [InlineData("actor")]
    [InlineData("capture")]
    [InlineData("protocol")]
    [InlineData("time")]
    public async Task Enrollment_response_bindings_are_validated(string kind)
    {
        Authenticate("Admin", "11");
        _mock.Enroll = (c, _) => Task.FromResult(kind switch
        {
            "target" => MockRecognitionComponent.Enrollment(c) with { StudentId = 2 },
            "request" => MockRecognitionComponent.Enrollment(c) with { RequestId = Guid.NewGuid() },
            "actor" => MockRecognitionComponent.Enrollment(c) with { ActorUserId = 22 },
            "capture" => MockRecognitionComponent.Enrollment(c) with { CaptureId = Guid.NewGuid() },
            "protocol" => MockRecognitionComponent.Enrollment(c) with { ContractVersion = "unsupported" },
            _ => MockRecognitionComponent.Enrollment(c) with { CompletedAtUtc = c.IssuedAtUtc.AddSeconds(-1) }
        });
        await AssertOutcome(await Post(EnrollPath), 502, "InvalidComponentResponse");
    }

    [Fact]
    public async Task Component_deadline_cancels_slow_work_without_attendance_or_exception_leaks()
    {
        _mock.Recognize = async (c, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return MockRecognitionComponent.Match(c);
        };
        await AssertOutcome(await Post(), 504, "ServiceTimeout");
    }

    [Theory]
    [InlineData("/api/recognition/sessions/999/recognize", 404, "SessionNotFound")]
    [InlineData("/api/recognition/students/999/enrollment", 404, "StudentNotFound")]
    public async Task Missing_resources_do_not_call_component(string path, int status, string code)
    {
        Authenticate("Admin", "11");
        await AssertOutcome(await Post(path), status, code);
        Assert.Equal(0, _mock.EnrollmentCalls); Assert.Equal(0, _mock.RecognitionCalls);
    }

    [Theory]
    [InlineData("/api/recognition/sessions/0/recognize")]
    [InlineData("/api/recognition/students/-1/enrollment")]
    public async Task Invalid_route_ids_return_400(string path)
    {
        Authenticate("Admin", "11");
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(path)).StatusCode);
        Assert.Equal(0, _mock.RecognitionCalls); Assert.Equal(0, _mock.EnrollmentCalls);
    }

    [Fact]
    public async Task Swagger_documents_disabled_contract_and_closed_request_schemas()
    {
        using var host = new SecurityApiFactory { EnvironmentName = "Development" };
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var doc = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        foreach (var path in new[] { "/api/recognition/students/{studentId}/enrollment", "/api/recognition/sessions/{sessionId}/recognize" })
        {
            var op = doc.GetProperty("paths").GetProperty(path).GetProperty("post");
            Assert.Contains("attendance is never recorded", op.GetProperty("description").GetString());
            foreach (var status in new[] { "200", "400", "401", "403", "404", "409", "422", "502", "503", "504" })
                Assert.True(op.GetProperty("responses").TryGetProperty(status, out _));
        }
        foreach (var schema in new[] { "RecognitionRequestDto", "RecognitionEnrollmentRequestDto" })
        {
            var definition = doc.GetProperty("components").GetProperty("schemas").GetProperty(schema);
            Assert.Contains(definition.GetProperty("required").EnumerateArray(), p => p.GetString() == "captureId");
            var properties = definition.GetProperty("properties");
            Assert.Equal("captureId", Assert.Single(properties.EnumerateObject()).Name);
        }
    }

    [Fact]
    public void Component_wire_contract_round_trips_with_explicit_string_outcomes_and_optional_confidence()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var command = new RecognitionCommand(RecognitionWorkflow.ContractVersion, Guid.NewGuid(), _capture,
            22, 2, 1, 1, _clock.Now);
        var result = MockRecognitionComponent.Match(command, confidence: null);
        var json = JsonSerializer.Serialize(result, options);
        var wire = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal("Matched", wire.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, wire.GetProperty("confidence").ValueKind);
        Assert.Equal(result, JsonSerializer.Deserialize<RecognitionComponentResult>(json, options));
        Assert.Equal(command, JsonSerializer.Deserialize<RecognitionCommand>(JsonSerializer.Serialize(command, options), options));
        var enrollment = new EnrollmentCommand(RecognitionWorkflow.ContractVersion, Guid.NewGuid(), _capture, 11, 1, _clock.Now);
        var enrolled = MockRecognitionComponent.Enrollment(enrollment);
        Assert.Equal(enrollment, JsonSerializer.Deserialize<EnrollmentCommand>(JsonSerializer.Serialize(enrollment, options), options));
        Assert.Equal(enrolled, JsonSerializer.Deserialize<EnrollmentComponentResult>(JsonSerializer.Serialize(enrolled, options), options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RecognitionComponentResult>(json.Replace("Matched", "invented-outcome"), options));
    }

    [Fact]
    public async Task Default_host_uses_unavailable_adapter_even_in_development()
    {
        using var host = new SecurityApiFactory { EnvironmentName = "Development", TestClock = _clock };
        host.ResetDatabase();
        using (var scope = host.Services.CreateScope())
        {
            Assert.IsType<UnavailableRecognitionComponent>(scope.ServiceProvider.GetRequiredService<IRecognitionComponent>());
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s = db.AttendanceSessions.Find(2)!; s.StartsAtUtc = _clock.Now.UtcDateTime.AddMinutes(-1); s.EndsAtUtc = _clock.Now.UtcDateTime.AddMinutes(1); db.SaveChanges();
        }
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new("Bearer", host.Token("Admin", "11"));
        foreach (var path in new[] { EnrollPath, RecognizePath })
        {
            var response = await client.PostAsJsonAsync(path, new { captureId = _capture });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isSimulation").GetBoolean());
        }
    }

    public void Dispose() { _client.Dispose(); _factory.Dispose(); }
    private sealed class RecognitionClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
