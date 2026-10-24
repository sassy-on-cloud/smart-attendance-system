using SmartAttendance.API.Recognition;

namespace SmartAttendance.IntegrationTests;

// Deterministic test double only: no images, templates, recognition algorithm, network or persistence.
public sealed class MockRecognitionComponent : IRecognitionComponent
{
    public int RecognitionCalls { get; private set; }
    public int EnrollmentCalls { get; private set; }
    public RecognitionCommand? LastRecognition { get; private set; }
    public EnrollmentCommand? LastEnrollment { get; private set; }
    public Func<RecognitionCommand, CancellationToken, Task<RecognitionComponentResult>> Recognize { get; set; } =
        (c, _) => Task.FromResult(Match(c));
    public Func<EnrollmentCommand, CancellationToken, Task<EnrollmentComponentResult>> Enroll { get; set; } =
        (c, _) => Task.FromResult(Enrollment(c));

    public static RecognitionComponentResult Match(RecognitionCommand c, int? studentId = 1, double? confidence = .9,
        ComponentOutcome outcome = ComponentOutcome.Matched) => new(c.ContractVersion, c.RequestId, c.CaptureId,
            c.ActorUserId, c.AttendanceSessionId, c.SessionVersion, c.IssuedAtUtc, outcome, studentId, confidence, true);
    public static EnrollmentComponentResult Enrollment(EnrollmentCommand c) => new(c.ContractVersion, c.RequestId,
        c.CaptureId, c.ActorUserId, c.StudentId, c.IssuedAtUtc, ComponentOutcome.Enrolled, true);

    public Task<RecognitionComponentResult> RecognizeAsync(RecognitionCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RecognitionCalls++; LastRecognition = command;
        return Recognize(command, cancellationToken);
    }
    public Task<EnrollmentComponentResult> EnrollAsync(EnrollmentCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnrollmentCalls++; LastEnrollment = command;
        return Enroll(command, cancellationToken);
    }
}
