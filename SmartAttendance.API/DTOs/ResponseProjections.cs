using System.Linq.Expressions;
using SmartAttendance.API.Models;

namespace SmartAttendance.API.DTOs;

// SQL projections keep entities and unapproved properties away from serialization.
internal static class ResponseProjections
{
    public static readonly Expression<Func<User, UserResponseDto>> User = user => new UserResponseDto
    {
        UserId = user.UserId,
        Name = user.Name,
        Email = user.Email,
        Role = user.Role
    };

    public static readonly Expression<Func<Student, StudentResponseDto>> Student = student => new StudentResponseDto
    {
        StudentId = student.StudentId,
        UserId = student.UserId,
        RollNumber = student.RollNumber,
        Department = student.Department,
        Semester = student.Semester,
        Name = student.User != null ? student.User.Name : "",
        Email = student.User != null ? student.User.Email : ""
    };

    public static readonly Expression<Func<Course, CourseResponseDto>> Course = course => new CourseResponseDto
    {
        CourseId = course.CourseId,
        CourseName = course.CourseName,
        CourseCode = course.CourseCode
    };

    public static readonly Expression<Func<AttendanceSession, AttendanceSessionResponseDto>> Session = session => new AttendanceSessionResponseDto
    {
        State = session.State, Version = session.Version, CreatedByUserId = session.CreatedByUserId,
        CreatedAtUtc = session.CreatedAtUtc, LastModifiedByUserId = session.LastModifiedByUserId,
        LastModifiedAtUtc = session.LastModifiedAtUtc,
        AttendanceSessionId = session.AttendanceSessionId, CourseId = session.CourseId,
        StartsAtUtc = session.StartsAtUtc, EndsAtUtc = session.EndsAtUtc, IsLegacy = session.IsLegacy
    };

    public static readonly Expression<Func<Attendance, AttendanceResponseDto>> Attendance = attendance => new AttendanceResponseDto
    {
        Version = attendance.Version, RecordedByUserId = attendance.RecordedByUserId,
        LastModifiedByUserId = attendance.LastModifiedByUserId, LastModifiedAtUtc = attendance.LastModifiedAtUtc,
        AttendanceId = attendance.AttendanceId,
        AttendanceSessionId = attendance.AttendanceSessionId,
        IsLegacy = attendance.Session.IsLegacy,
        StudentId = attendance.StudentId,
        CourseId = attendance.CourseId,
        Date = attendance.Date,
        Status = attendance.Status,
        Method = attendance.Method
    };

    public static readonly Expression<Func<AttendanceHistory, AttendanceHistoryResponseDto>> History = h => new AttendanceHistoryResponseDto
    {
        AttendanceHistoryId = h.AttendanceHistoryId, ActorUserId = h.ActorUserId, ChangedAtUtc = h.ChangedAtUtc,
        ChangeType = h.ChangeType, PreviousStatus = h.PreviousStatus, PreviousMethod = h.PreviousMethod,
        Status = h.Status, Method = h.Method, Reason = h.Reason, Version = h.Version
    };
    public static readonly Expression<Func<AttendanceSessionHistory, AttendanceSessionHistoryResponseDto>> SessionHistory = h => new AttendanceSessionHistoryResponseDto
    {
        AttendanceSessionHistoryId = h.AttendanceSessionHistoryId, ActorUserId = h.ActorUserId,
        ChangedAtUtc = h.ChangedAtUtc, PreviousState = h.PreviousState, State = h.State,
        PreviousStartsAtUtc = h.PreviousStartsAtUtc, PreviousEndsAtUtc = h.PreviousEndsAtUtc,
        StartsAtUtc = h.StartsAtUtc, EndsAtUtc = h.EndsAtUtc, Reason = h.Reason, Version = h.Version
    };
}
