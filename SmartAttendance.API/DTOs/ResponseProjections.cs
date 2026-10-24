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

    public static readonly Expression<Func<Attendance, AttendanceResponseDto>> Attendance = attendance => new AttendanceResponseDto
    {
        AttendanceId = attendance.AttendanceId,
        StudentId = attendance.StudentId,
        CourseId = attendance.CourseId,
        Date = attendance.Date,
        Status = attendance.Status,
        Method = attendance.Method
    };
}
