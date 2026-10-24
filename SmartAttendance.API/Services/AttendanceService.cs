using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;
using SmartAttendance.API.DTOs;
using SmartAttendance.API.Models;

namespace SmartAttendance.API.Services;

public enum AttendanceWriteOutcome
{
    Created, Corrected, MissingSession, InvalidSession, MissingStudent, InvalidStudent,
    NotEnrolled, Forbidden, Duplicate, NotFound, Conflict
}
public record AttendanceWriteResult(AttendanceWriteOutcome Outcome, AttendanceResponseDto? Attendance = null);

public class AttendanceService(AppDbContext context, TimeProvider clock, AttendanceAccess access)
{
    public async Task<AttendanceWriteResult> MarkManualAsync(CreateAttendanceDto dto, bool isAdmin,
        int? actorUserId, CancellationToken cancellationToken)
    {
        if (!await access.IsStaffActorAsync(isAdmin, actorUserId, cancellationToken))
            return new(AttendanceWriteOutcome.Forbidden);
        var session = await context.AttendanceSessions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.AttendanceSessionId == dto.AttendanceSessionId, cancellationToken);
        if (session is null) return new(AttendanceWriteOutcome.MissingSession);
        if (!await access.CanManageCourseAsync(isAdmin, actorUserId, session.CourseId, cancellationToken))
            return new(AttendanceWriteOutcome.Forbidden);
        if (session.IsLegacy) return new(AttendanceWriteOutcome.InvalidSession);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // A transactional write locks the session row, without changing its public version.
        // Session updates and corrections take the same gate, preventing mark/cancel/reschedule races.
        var gated = await context.AttendanceSessions
            .Where(s => s.AttendanceSessionId == dto.AttendanceSessionId && !s.IsLegacy && s.State == AttendanceSessionStates.Open)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Version, s => s.Version), cancellationToken);
        if (gated == 0) return new(AttendanceWriteOutcome.Conflict);

        var student = await context.Students.AsNoTracking().Where(s => s.StudentId == dto.StudentId)
            .Select(s => new { Role = s.User != null ? s.User.Role : "" }).SingleOrDefaultAsync(cancellationToken);
        if (student is null) return new(AttendanceWriteOutcome.MissingStudent);
        if (student.Role != "Student") return new(AttendanceWriteOutcome.InvalidStudent);
        if (!await context.CourseEnrollments.AnyAsync(e => e.StudentId == dto.StudentId &&
            e.CourseId == session.CourseId && !e.IsLegacyInferred, cancellationToken))
            return new(AttendanceWriteOutcome.NotEnrolled);
        if (await context.Attendances.AnyAsync(a => a.StudentId == dto.StudentId &&
            a.AttendanceSessionId == dto.AttendanceSessionId, cancellationToken))
            return new(AttendanceWriteOutcome.Duplicate);

        var attendance = new Attendance
        {
            StudentId = dto.StudentId, CourseId = session.CourseId, AttendanceSessionId = session.AttendanceSessionId,
            Date = clock.GetUtcNow().UtcDateTime, Status = dto.Status, Method = AttendanceMethods.Manual,
            RecordedByUserId = actorUserId, Version = 1
        };
        context.Attendances.Add(attendance);
        context.AttendanceHistory.Add(new AttendanceHistory
        {
            Attendance = attendance, ActorUserId = actorUserId!.Value, ChangedAtUtc = attendance.Date,
            ChangeType = "Created", Status = attendance.Status, Method = attendance.Method,
            Reason = "Initial manual mark.", Version = 1
        });
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            context.ChangeTracker.Clear();
            // The unique key remains the final guard even if another writer bypasses the service.
            if (await context.Attendances.AnyAsync(a => a.StudentId == dto.StudentId &&
                a.AttendanceSessionId == dto.AttendanceSessionId, cancellationToken))
                return new(AttendanceWriteOutcome.Duplicate);
            throw;
        }
        var response = await Project(attendance.AttendanceId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(AttendanceWriteOutcome.Created, response);
    }

    public async Task<AttendanceWriteResult> CorrectManualAsync(int id, CorrectAttendanceDto dto, bool isAdmin,
        int? actorUserId, CancellationToken cancellationToken)
    {
        if (!await access.IsStaffActorAsync(isAdmin, actorUserId, cancellationToken))
            return new(AttendanceWriteOutcome.Forbidden);
        var target = await context.Attendances.AsNoTracking().Where(a => a.AttendanceId == id)
            .Select(a => new { a.AttendanceSessionId, a.CourseId, a.Session.IsLegacy }).SingleOrDefaultAsync(cancellationToken);
        if (target is null) return new(AttendanceWriteOutcome.NotFound);
        if (!await access.CanManageCourseAsync(isAdmin, actorUserId, target.CourseId, cancellationToken))
            return new(AttendanceWriteOutcome.Forbidden);
        if (target.IsLegacy) return new(AttendanceWriteOutcome.InvalidSession);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var gated = await context.AttendanceSessions.Where(s => s.AttendanceSessionId == target.AttendanceSessionId &&
            !s.IsLegacy && s.State != AttendanceSessionStates.Cancelled)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Version, s => s.Version), cancellationToken);
        if (gated == 0) return new(AttendanceWriteOutcome.Conflict);
        var attendance = await context.Attendances.SingleAsync(a => a.AttendanceId == id, cancellationToken);
        if (attendance.Version == int.MaxValue || attendance.Version != dto.ExpectedVersion ||
            (attendance.Status == dto.Status && attendance.Method == AttendanceMethods.Manual))
            return new(AttendanceWriteOutcome.Conflict);
        if (!await context.Students.AnyAsync(s => s.StudentId == attendance.StudentId &&
            s.User != null && s.User.Role == "Student", cancellationToken))
            return new(AttendanceWriteOutcome.InvalidStudent);
        if (!await context.CourseEnrollments.AnyAsync(e => e.StudentId == attendance.StudentId &&
            e.CourseId == attendance.CourseId && !e.IsLegacyInferred, cancellationToken))
            return new(AttendanceWriteOutcome.NotEnrolled);
        var previousStatus = attendance.Status;
        var previousMethod = attendance.Method;
        attendance.Status = dto.Status;
        attendance.Method = AttendanceMethods.Manual;
        attendance.LastModifiedByUserId = actorUserId;
        attendance.LastModifiedAtUtc = clock.GetUtcNow().UtcDateTime;
        attendance.Version++;
        context.AttendanceHistory.Add(new AttendanceHistory
        {
            AttendanceId = attendance.AttendanceId, ActorUserId = actorUserId!.Value,
            ChangedAtUtc = attendance.LastModifiedAtUtc.Value, ChangeType = "Corrected",
            PreviousStatus = previousStatus, PreviousMethod = previousMethod,
            Status = attendance.Status, Method = attendance.Method, Reason = dto.Reason.Trim(), Version = attendance.Version
        });
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return new(AttendanceWriteOutcome.Conflict); }
        var response = await Project(id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(AttendanceWriteOutcome.Corrected, response);
    }

    private Task<AttendanceResponseDto> Project(int id, CancellationToken cancellationToken) =>
        context.Attendances.AsNoTracking().Where(a => a.AttendanceId == id)
            .Select(ResponseProjections.Attendance).SingleAsync(cancellationToken);
}
