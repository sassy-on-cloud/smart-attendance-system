using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.API.Models;

namespace SmartAttendance.API.Data.Configurations;

public class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        builder.HasIndex(s => s.UserId).IsUnique();
        builder.HasOne(s => s.User).WithOne().HasForeignKey<Student>(s => s.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}

public class CourseEnrollmentConfiguration : IEntityTypeConfiguration<CourseEnrollment>
{
    public void Configure(EntityTypeBuilder<CourseEnrollment> builder)
    {
        builder.HasKey(e => new { e.StudentId, e.CourseId });
        builder.HasOne(e => e.Student).WithMany().HasForeignKey(e => e.StudentId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Course).WithMany().HasForeignKey(e => e.CourseId).OnDelete(DeleteBehavior.NoAction);
    }
}

public class TeacherCourseAssignmentConfiguration : IEntityTypeConfiguration<TeacherCourseAssignment>
{
    public void Configure(EntityTypeBuilder<TeacherCourseAssignment> builder)
    {
        builder.HasKey(a => new { a.TeacherUserId, a.CourseId });
        builder.HasOne(a => a.Teacher).WithMany().HasForeignKey(a => a.TeacherUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(a => a.Course).WithMany().HasForeignKey(a => a.CourseId).OnDelete(DeleteBehavior.NoAction);
    }
}

public class AttendanceSessionConfiguration : IEntityTypeConfiguration<AttendanceSession>
{
    public void Configure(EntityTypeBuilder<AttendanceSession> builder)
    {
        builder.HasKey(s => s.AttendanceSessionId);
        builder.HasAlternateKey(s => new { s.AttendanceSessionId, s.CourseId });
        builder.HasOne(s => s.Course).WithMany().HasForeignKey(s => s.CourseId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(s => new { s.CourseId, s.StartsAtUtc }); // Deliberately not unique by day/time.
        builder.HasIndex(s => s.LegacyAttendanceId).IsUnique().HasFilter("[LegacyAttendanceId] IS NOT NULL");
        builder.ToTable(t => t.HasCheckConstraint("CK_AttendanceSessions_Meeting", """
            ([IsLegacy] = 1 AND [StartsAtUtc] IS NULL AND [EndsAtUtc] IS NULL AND [LegacyAttendanceId] IS NOT NULL)
            OR ([IsLegacy] = 0 AND [StartsAtUtc] IS NOT NULL AND [EndsAtUtc] IS NOT NULL
                AND [EndsAtUtc] > [StartsAtUtc] AND [LegacyAttendanceId] IS NULL)
            """));
    }
}
