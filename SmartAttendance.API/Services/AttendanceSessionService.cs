using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;
using SmartAttendance.API.DTOs;
using SmartAttendance.API.Models;

namespace SmartAttendance.API.Services;

public enum SessionWriteOutcome { Created, Updated, NotFound, Invalid, Forbidden, Conflict }
public record SessionWriteResult(SessionWriteOutcome Outcome, AttendanceSessionResponseDto? Session = null);

public class AttendanceSessionService(AppDbContext context, TimeProvider clock, AttendanceAccess access)
{
    public async Task<SessionWriteResult> CreateAsync(CreateAttendanceSessionDto dto, bool isAdmin,
        int? actorUserId, CancellationToken cancellationToken)
    {
        if (!await access.IsStaffActorAsync(isAdmin, actorUserId, cancellationToken) ||
            !await access.CanManageCourseAsync(isAdmin, actorUserId, dto.CourseId, cancellationToken))
            return new(SessionWriteOutcome.Forbidden);
        if (!await context.Courses.AnyAsync(c => c.CourseId == dto.CourseId, cancellationToken))
            return new(SessionWriteOutcome.NotFound);
        var session = new AttendanceSession
        {
            CourseId = dto.CourseId, StartsAtUtc = dto.StartsAt!.Value.UtcDateTime, EndsAtUtc = dto.EndsAt!.Value.UtcDateTime,
            CreatedByUserId = actorUserId, CreatedAtUtc = clock.GetUtcNow().UtcDateTime
        };
        context.AttendanceSessions.Add(session);
        context.AttendanceSessionHistory.Add(new AttendanceSessionHistory
        {
            Session = session, ActorUserId = actorUserId!.Value, ChangedAtUtc = session.CreatedAtUtc.Value,
            State = session.State, StartsAtUtc = session.StartsAtUtc.Value, EndsAtUtc = session.EndsAtUtc.Value,
            Reason = "Initial session creation.", Version = 1
        });
        await context.SaveChangesAsync(cancellationToken);
        return new(SessionWriteOutcome.Created, await Project(session.AttendanceSessionId, cancellationToken));
    }

    public async Task<SessionWriteResult> UpdateAsync(int id, UpdateAttendanceSessionDto dto, bool isAdmin,
        int? actorUserId, CancellationToken cancellationToken)
    {
        if (!await access.IsStaffActorAsync(isAdmin, actorUserId, cancellationToken))
            return new(SessionWriteOutcome.Forbidden);
        var target = await context.AttendanceSessions.AsNoTracking().SingleOrDefaultAsync(s => s.AttendanceSessionId == id, cancellationToken);
        if (target is null) return new(SessionWriteOutcome.NotFound);
        if (!await access.CanManageCourseAsync(isAdmin, actorUserId, target.CourseId, cancellationToken))
            return new(SessionWriteOutcome.Forbidden);
        if (target.IsLegacy) return new(SessionWriteOutcome.Invalid);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // This gate is also taken by marking and corrections. Read mutable state only after acquiring it.
        var gated = await context.AttendanceSessions.Where(s => s.AttendanceSessionId == id && s.Version == dto.ExpectedVersion)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.Version, s => s.Version), cancellationToken);
        if (gated == 0) return new(SessionWriteOutcome.Conflict);
        var session = await context.AttendanceSessions.SingleAsync(s => s.AttendanceSessionId == id, cancellationToken);
        if (session.Version == int.MaxValue || session.State == AttendanceSessionStates.Cancelled)
            return new(SessionWriteOutcome.Conflict);
        var startsAt = dto.StartsAt!.Value.UtcDateTime;
        var endsAt = dto.EndsAt!.Value.UtcDateTime;
        var timesChanged = session.StartsAtUtc != startsAt || session.EndsAtUtc != endsAt;
        if ((timesChanged && await context.Attendances.AnyAsync(a => a.AttendanceSessionId == id, cancellationToken)) ||
            (!timesChanged && session.State == dto.State))
            return new(SessionWriteOutcome.Conflict);
        context.AttendanceSessionHistory.Add(new AttendanceSessionHistory
        {
            AttendanceSessionId = id, ActorUserId = actorUserId!.Value, ChangedAtUtc = clock.GetUtcNow().UtcDateTime,
            PreviousState = session.State, State = dto.State, PreviousStartsAtUtc = session.StartsAtUtc,
            PreviousEndsAtUtc = session.EndsAtUtc, StartsAtUtc = startsAt, EndsAtUtc = endsAt,
            Reason = dto.Reason.Trim(), Version = session.Version + 1
        });
        session.StartsAtUtc = startsAt; session.EndsAtUtc = endsAt; session.State = dto.State;
        session.LastModifiedByUserId = actorUserId; session.LastModifiedAtUtc = clock.GetUtcNow().UtcDateTime;
        session.Version++;
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return new(SessionWriteOutcome.Conflict); }
        var response = await Project(id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(SessionWriteOutcome.Updated, response);
    }

    private Task<AttendanceSessionResponseDto> Project(int id, CancellationToken cancellationToken) =>
        context.AttendanceSessions.AsNoTracking().Where(s => s.AttendanceSessionId == id)
            .Select(ResponseProjections.Session).SingleAsync(cancellationToken);
}
