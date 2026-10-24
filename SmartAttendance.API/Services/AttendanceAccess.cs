using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;

namespace SmartAttendance.API.Services;

public class AttendanceAccess(AppDbContext context)
{
    public Task<bool> IsStaffActorAsync(bool isAdmin, int? userId, CancellationToken cancellationToken) =>
        context.Users.AnyAsync(u => u.UserId == userId && u.Role == (isAdmin ? "Admin" : "Teacher"), cancellationToken);

    public Task<bool> CanManageCourseAsync(bool isAdmin, int? userId, int courseId, CancellationToken cancellationToken) =>
        isAdmin ? Task.FromResult(true) : context.TeacherCourseAssignments
            .AnyAsync(a => a.CourseId == courseId && a.TeacherUserId == userId, cancellationToken);
}
