using System.Text.Json.Serialization;

namespace SmartAttendance.API.Recognition;

[JsonConverter(typeof(JsonStringEnumConverter<ComponentOutcome>))]
public enum ComponentOutcome
{
    Unavailable, InvalidCapture, NoFace, UnknownFace, MultipleFaces, AmbiguousMatch, AlreadyEnrolled, Enrolled, Matched
}

// Backend-to-component messages only. Never bind these types from a browser request.
public sealed record EnrollmentCommand(string ContractVersion, Guid RequestId, Guid CaptureId,
    int ActorUserId, int StudentId, DateTimeOffset IssuedAtUtc);

public sealed record RecognitionCommand(string ContractVersion, Guid RequestId, Guid CaptureId,
    int ActorUserId, int AttendanceSessionId, int SessionVersion, int CourseId, DateTimeOffset IssuedAtUtc);

public sealed record EnrollmentComponentResult(string ContractVersion, Guid RequestId, Guid CaptureId,
    int ActorUserId, int StudentId, DateTimeOffset CompletedAtUtc, ComponentOutcome Outcome, bool IsSimulation);

public sealed record RecognitionComponentResult(string ContractVersion, Guid RequestId, Guid CaptureId,
    int ActorUserId, int AttendanceSessionId, int SessionVersion, DateTimeOffset CompletedAtUtc,
    ComponentOutcome Outcome, int? StudentId, double? Confidence, bool IsSimulation);

/// <summary>
/// Trusted local integration boundary. Implementations must authenticate the component, resolve
/// actor-bound fresh captures and validate its protocol. There is deliberately no network implementation.
/// Do not install AI models/services. See docs/RECOGNITION_INTEGRATION.md before implementing.
/// </summary>
public interface IRecognitionComponent
{
    Task<EnrollmentComponentResult> EnrollAsync(EnrollmentCommand command, CancellationToken cancellationToken);
    Task<RecognitionComponentResult> RecognizeAsync(RecognitionCommand command, CancellationToken cancellationToken);
}

public sealed class UnavailableRecognitionComponent : IRecognitionComponent
{
    public Task<EnrollmentComponentResult> EnrollAsync(EnrollmentCommand c, CancellationToken cancellationToken) =>
        Task.FromResult(new EnrollmentComponentResult(c.ContractVersion, c.RequestId, c.CaptureId,
            c.ActorUserId, c.StudentId, c.IssuedAtUtc, ComponentOutcome.Unavailable, false));

    public Task<RecognitionComponentResult> RecognizeAsync(RecognitionCommand c, CancellationToken cancellationToken) =>
        Task.FromResult(new RecognitionComponentResult(c.ContractVersion, c.RequestId, c.CaptureId,
            c.ActorUserId, c.AttendanceSessionId, c.SessionVersion, c.IssuedAtUtc,
            ComponentOutcome.Unavailable, null, null, false));
}
