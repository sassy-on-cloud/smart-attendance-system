using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;
using SmartAttendance.API.Models;

namespace SmartAttendance.IntegrationTests;

public sealed class FixedReportClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => ReportingTestData.Now;
}

public sealed record ReportingIds(int Admin, int Teacher, int OtherTeacher, int EmptyTeacher,
    int StudentUser, int OtherStudentUser, int InferredStudentUser, int Student, int OtherStudent, int InferredStudent,
    int HiddenStudent, int Course, int SecondCourse, int EmptyCourse, int HiddenCourse,
    int FirstSession, int SecondSession, int ExcusedSession, int FutureSession, int OpenSession, int LegacySession);

public static class ReportingTestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    public static async Task<ReportingIds> SeedAsync(AppDbContext db)
    {
        var admin = new User { Name = "Report Admin", Role = "Admin", Email = "admin@example.test" };
        var teacher = new User { Name = "Assigned Teacher", Role = "Teacher", Email = "teacher@example.test" };
        var one = new User { Name = "Student One", Role = "Student", Email = "one@example.test" };
        var two = new User { Name = "Student Two", Role = "Student", Email = "two@example.test" };
        var inferredUser = new User { Name = "Inferred Student", Role = "Student", Email = "inferred@example.test" };
        var hiddenUser = new User { Name = "Hidden Student", Role = "Student", Email = "hidden@example.test" };
        var otherTeacher = new User { Name = "Other Teacher", Role = "Teacher", Email = "otherteacher@example.test" };
        var emptyTeacher = new User { Name = "Unassigned Teacher", Role = "Teacher", Email = "emptyteacher@example.test" };
        db.Users.AddRange(admin, teacher, one, two, inferredUser, hiddenUser, otherTeacher, emptyTeacher);
        await db.SaveChangesAsync();
        var student = new Student { UserId = one.UserId, RollNumber = "R1", Department = "Computing", Semester = 1 };
        var otherStudent = new Student { UserId = two.UserId, RollNumber = "R2", Department = "Computing", Semester = 1 };
        var inferred = new Student { UserId = inferredUser.UserId, RollNumber = "R3", Department = "Computing", Semester = 1 };
        var hidden = new Student { UserId = hiddenUser.UserId, RollNumber = "R4", Department = "Computing", Semester = 1 };
        var course = new Course { CourseCode = "C1", CourseName = "Computing" };
        var second = new Course { CourseCode = "C2", CourseName = "Math" };
        var empty = new Course { CourseCode = "C3", CourseName = "No meetings" };
        var secret = new Course { CourseCode = "C4", CourseName = "Other teacher only" };
        db.Students.AddRange(student, otherStudent, inferred, hidden);
        db.Courses.AddRange(course, second, empty, secret); await db.SaveChangesAsync();
        db.CourseEnrollments.AddRange(
            new CourseEnrollment { StudentId = student.StudentId, CourseId = course.CourseId },
            new CourseEnrollment { StudentId = otherStudent.StudentId, CourseId = course.CourseId },
            new CourseEnrollment { StudentId = student.StudentId, CourseId = second.CourseId },
            new CourseEnrollment { StudentId = student.StudentId, CourseId = empty.CourseId },
            new CourseEnrollment { StudentId = hidden.StudentId, CourseId = secret.CourseId },
            new CourseEnrollment { StudentId = inferred.StudentId, CourseId = course.CourseId, IsLegacyInferred = true });
        db.TeacherCourseAssignments.AddRange(
            new TeacherCourseAssignment { TeacherUserId = teacher.UserId, CourseId = course.CourseId },
            new TeacherCourseAssignment { TeacherUserId = teacher.UserId, CourseId = second.CourseId },
            new TeacherCourseAssignment { TeacherUserId = teacher.UserId, CourseId = empty.CourseId },
            new TeacherCourseAssignment { TeacherUserId = otherTeacher.UserId, CourseId = secret.CourseId });
        AttendanceSession Meeting(Course c, int day, int hour, string state = "Closed") => new()
        {
            CourseId = c.CourseId, StartsAtUtc = new DateTime(2026, 10, day, hour, 0, 0, DateTimeKind.Utc),
            EndsAtUtc = new DateTime(2026, 10, day, hour + 1, 0, 0, DateTimeKind.Utc), State = state
        };
        var first = Meeting(course, 20, 4); var sameDay = Meeting(course, 20, 6);
        var excused = Meeting(course, 21, 4); var missing = Meeting(course, 22, 4);
        var absent = Meeting(course, 23, 4); var unverified = Meeting(course, 23, 6);
        var open = Meeting(course, 20, 8, "Open"); var cancelled = Meeting(course, 20, 10, "Cancelled");
        var future = Meeting(course, 25, 4);
        var legacy = new AttendanceSession { CourseId = course.CourseId, IsLegacy = true, LegacyAttendanceId = 10001 };
        var legacyTwo = new AttendanceSession { CourseId = course.CourseId, IsLegacy = true, LegacyAttendanceId = 10002 };
        var secondMeeting = Meeting(second, 22, 8); var hiddenMeeting = Meeting(secret, 20, 12);
        db.AttendanceSessions.AddRange(first, sameDay, excused, missing, absent, unverified, open, cancelled, future, legacy, legacyTwo, secondMeeting, hiddenMeeting);
        await db.SaveChangesAsync();
        void Mark(Student s, AttendanceSession session, string status) => db.Attendances.Add(new Attendance
        {
            StudentId = s.StudentId, CourseId = session.CourseId, AttendanceSessionId = session.AttendanceSessionId,
            Date = Now.AddHours(-1).UtcDateTime, Status = status, Method = "Manual"
        });
        Mark(student, first, "Present"); Mark(student, sameDay, "Late"); Mark(student, excused, "Excused");
        Mark(student, absent, "Absent"); Mark(student, unverified, "Unverified");
        Mark(student, open, "Present"); Mark(student, cancelled, "Present"); Mark(student, future, "Present");
        Mark(student, legacy, "Present"); Mark(student, legacyTwo, "Present"); Mark(student, secondMeeting, "Present");
        Mark(otherStudent, first, "Absent"); Mark(otherStudent, sameDay, "Present"); Mark(otherStudent, unverified, "Absent");
        Mark(otherStudent, missing, "Excused"); Mark(otherStudent, open, "Present"); Mark(otherStudent, cancelled, "Present");
        Mark(inferred, first, "Present"); Mark(hidden, hiddenMeeting, "Present");
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        return new(admin.UserId, teacher.UserId, otherTeacher.UserId, emptyTeacher.UserId, one.UserId, two.UserId, inferredUser.UserId,
            student.StudentId, otherStudent.StudentId, inferred.StudentId, hidden.StudentId, course.CourseId, second.CourseId, empty.CourseId, secret.CourseId,
            first.AttendanceSessionId, sameDay.AttendanceSessionId, excused.AttendanceSessionId, future.AttendanceSessionId, open.AttendanceSessionId, legacy.AttendanceSessionId);
    }
}
