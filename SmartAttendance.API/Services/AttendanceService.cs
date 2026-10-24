using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;
using SmartAttendance.API.DTOs;
using SmartAttendance.API.Models;

namespace SmartAttendance.API.Services;

public enum AttendanceWriteOutcome { Created, InvalidSession, NotEnrolled, Forbidden, Duplicate }
public record AttendanceWriteResult(AttendanceWriteOutcome Outcome, AttendanceResponseDto? Attendance = null);

public class AttendanceService(AppDbContext context, TimeProvider clock)
{
    public async Task<AttendanceWriteResult> MarkManualAsync(CreateAttendanceDto dto, bool isAdmin,
        int? actorUserId, CancellationToken cancellationToken)
    {
        var session = await context.AttendanceSessions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.AttendanceSessionId == dto.AttendanceSessionId, cancellationToken);
        if (session is null || session.IsLegacy)
            return new(AttendanceWriteOutcome.InvalidSession);
        if (!isAdmin && (!actorUserId.HasValue || !await context.TeacherCourseAssignments
            .AnyAsync(a => a.CourseId == session.CourseId && a.TeacherUserId == actorUserId, cancellationToken)))
            return new(AttendanceWriteOutcome.Forbidden);
        if (!await context.CourseEnrollments.AnyAsync(e => e.StudentId == dto.StudentId &&
            e.CourseId == session.CourseId && !e.IsLegacyInferred, cancellationToken))
            return new(AttendanceWriteOutcome.NotEnrolled);
        if (await context.Attendances.AnyAsync(a => a.StudentId == dto.StudentId &&
            a.AttendanceSessionId == dto.AttendanceSessionId, cancellationToken))
            return new(AttendanceWriteOutcome.Duplicate);

        var attendance = new Attendance
        {
            StudentId = dto.StudentId, CourseId = session.CourseId, AttendanceSessionId = session.AttendanceSessionId,
            Date = clock.GetUtcNow().UtcDateTime, Status = dto.Status, Method = AttendanceMethods.Manual
        };
        context.Attendances.Add(attendance);
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            // The unique database key is authoritative, including simultaneous writers.
            if (await context.Attendances.AsNoTracking().AnyAsync(a => a.StudentId == dto.StudentId &&
                a.AttendanceSessionId == dto.AttendanceSessionId, cancellationToken))
                return new(AttendanceWriteOutcome.Duplicate);
            throw;
        }
        var response = await context.Attendances.AsNoTracking().Where(a => a.AttendanceId == attendance.AttendanceId)
            .Select(ResponseProjections.Attendance).SingleAsync(cancellationToken);
        return new(AttendanceWriteOutcome.Created, response);
    }
}
