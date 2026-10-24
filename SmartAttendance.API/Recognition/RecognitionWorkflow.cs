using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;
using SmartAttendance.API.Models;
using SmartAttendance.API.Services;

namespace SmartAttendance.API.Recognition;

public sealed record RecognitionWorkflowResult(int HttpStatus, Guid RequestId, string Outcome,
    int? StudentId = null, double? Confidence = null, bool IsSimulation = false);

// Contract validation only: no enrollment persistence or attendance writer is enabled in Milestone 7.
public sealed class RecognitionWorkflow(AppDbContext db, AttendanceAccess access,
    IRecognitionComponent component, TimeProvider clock)
{
    public const string ContractVersion = "recognition-contract-v1";
    public static readonly TimeSpan ComponentTimeout = TimeSpan.FromSeconds(5);
    private bool Fresh(DateTimeOffset issued, DateTimeOffset completed) =>
        completed >= issued && completed <= clock.GetUtcNow() && clock.GetUtcNow() - issued <= TimeSpan.FromSeconds(30);

    public async Task<RecognitionWorkflowResult> EnrollAsync(int studentId, Guid captureId, int actorId, CancellationToken ct)
    {
        var requestId = Guid.NewGuid();
        RecognitionWorkflowResult Fail(int status, string outcome) => new(status, requestId, outcome);
        if (!await access.IsStaffActorAsync(true, actorId, ct)) return Fail(403, "Forbidden");
        var student = await db.Students.AsNoTracking().Where(s => s.StudentId == studentId)
            .Select(s => new { Role = s.User != null ? s.User.Role : "" }).SingleOrDefaultAsync(ct);
        if (student is null) return Fail(404, "StudentNotFound");
        if (student.Role != "Student") return Fail(400, "InvalidStudent");
        var command = new EnrollmentCommand(ContractVersion, requestId, captureId, actorId, studentId, clock.GetUtcNow());
        EnrollmentComponentResult result;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(ComponentTimeout);
        try { result = await component.EnrollAsync(command, deadline.Token).WaitAsync(deadline.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Fail(504, "ServiceTimeout"); }
        catch (Exception) when (!ct.IsCancellationRequested) { return Fail(503, "ServiceUnavailable"); }
        if (result is null || result.ContractVersion != ContractVersion || result.RequestId != requestId ||
            result.CaptureId != captureId || result.ActorUserId != actorId || result.StudentId != studentId ||
            !Fresh(command.IssuedAtUtc, result.CompletedAtUtc) || !Enum.IsDefined(result.Outcome))
            return Fail(502, "InvalidComponentResponse");
        if (!await access.IsStaffActorAsync(true, actorId, ct)) return Fail(403, "Forbidden");
        if (!await db.Students.AnyAsync(s => s.StudentId == studentId && s.User != null && s.User.Role == "Student", ct))
            return Fail(409, "StudentChanged");
        if (result.Outcome == ComponentOutcome.AlreadyEnrolled) return new(409, requestId, "AlreadyEnrolled", IsSimulation: result.IsSimulation);
        if (result.Outcome != ComponentOutcome.Enrolled) return Failure(result.Outcome, requestId, result.IsSimulation);
        // Even a putative real result cannot activate an unapproved implementation.
        return result.IsSimulation ? new(200, requestId, "SimulatedEnrollment", studentId, IsSimulation: true)
            : Fail(503, "IntegrationNotApproved");
    }

    public async Task<RecognitionWorkflowResult> RecognizeAsync(int sessionId, Guid captureId,
        bool isAdmin, int actorId, CancellationToken ct)
    {
        var requestId = Guid.NewGuid();
        RecognitionWorkflowResult Fail(int status, string outcome) => new(status, requestId, outcome);
        if (!await access.IsStaffActorAsync(isAdmin, actorId, ct)) return Fail(403, "Forbidden");
        var session = await db.AttendanceSessions.AsNoTracking().SingleOrDefaultAsync(s => s.AttendanceSessionId == sessionId, ct);
        if (session is null) return Fail(404, "SessionNotFound");
        if (!await access.CanManageCourseAsync(isAdmin, actorId, session.CourseId, ct)) return Fail(403, "Forbidden");
        if (!Active(session)) return Fail(409, "SessionNotActive");
        var command = new RecognitionCommand(ContractVersion, requestId, captureId, actorId,
            sessionId, session.Version, session.CourseId, clock.GetUtcNow());
        RecognitionComponentResult result;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(ComponentTimeout);
        try { result = await component.RecognizeAsync(command, deadline.Token).WaitAsync(deadline.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Fail(504, "ServiceTimeout"); }
        catch (Exception) when (!ct.IsCancellationRequested) { return Fail(503, "ServiceUnavailable"); }
        if (result is null || result.ContractVersion != ContractVersion || result.RequestId != requestId ||
            result.CaptureId != captureId || result.ActorUserId != actorId || result.AttendanceSessionId != sessionId ||
            result.SessionVersion != session.Version || !Fresh(command.IssuedAtUtc, result.CompletedAtUtc) ||
            !Enum.IsDefined(result.Outcome) ||
            (result.Confidence.HasValue && (!double.IsFinite(result.Confidence.Value) || result.Confidence < 0 || result.Confidence > 1)) ||
            (result.Outcome != ComponentOutcome.Matched && (result.StudentId.HasValue || result.Confidence.HasValue)) ||
            (result.Outcome == ComponentOutcome.Matched && result.StudentId is not > 0))
            return Fail(502, "InvalidComponentResponse");
        if (!await access.IsStaffActorAsync(isAdmin, actorId, ct) ||
            !await access.CanManageCourseAsync(isAdmin, actorId, session.CourseId, ct)) return Fail(403, "Forbidden");
        var current = await db.AttendanceSessions.AsNoTracking().SingleOrDefaultAsync(s => s.AttendanceSessionId == sessionId, ct);
        if (current is null || current.Version != session.Version || current.CourseId != session.CourseId || !Active(current))
            return Fail(409, "SessionChanged");
        if (result.Outcome != ComponentOutcome.Matched) return Failure(result.Outcome, requestId, result.IsSimulation);
        var studentId = result.StudentId!.Value;
        if (!await db.Students.AnyAsync(s => s.StudentId == studentId && s.User != null && s.User.Role == "Student", ct))
            return Fail(422, "IneligibleMatch");
        if (!await db.CourseEnrollments.AnyAsync(e => e.StudentId == studentId && e.CourseId == session.CourseId && !e.IsLegacyInferred, ct))
            return Fail(422, "IneligibleMatch");
        if (await db.Attendances.AnyAsync(a => a.StudentId == studentId && a.AttendanceSessionId == sessionId, ct))
            return Fail(409, "DuplicateAttendance");
        return result.IsSimulation ? new(200, requestId, "SimulatedMatch", studentId, result.Confidence, true)
            : Fail(503, "IntegrationNotApproved");
    }

    private bool Active(AttendanceSession session)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return !session.IsLegacy && session.State == AttendanceSessionStates.Open &&
            session.StartsAtUtc.HasValue && session.EndsAtUtc.HasValue && session.StartsAtUtc <= now && now < session.EndsAtUtc;
    }

    private static RecognitionWorkflowResult Failure(ComponentOutcome outcome, Guid requestId, bool simulation) => outcome switch
    {
        ComponentOutcome.Unavailable => new(503, requestId, "ServiceUnavailable", IsSimulation: simulation),
        ComponentOutcome.InvalidCapture => new(400, requestId, "InvalidCapture", IsSimulation: simulation),
        ComponentOutcome.NoFace or ComponentOutcome.UnknownFace or ComponentOutcome.MultipleFaces or ComponentOutcome.AmbiguousMatch =>
            new(422, requestId, outcome.ToString(), IsSimulation: simulation),
        _ => new(502, requestId, "InvalidComponentResponse")
    };
}
