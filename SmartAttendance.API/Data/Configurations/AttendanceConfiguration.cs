using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.API.Models;

namespace SmartAttendance.API.Data.Configurations;

public class AttendanceConfiguration : IEntityTypeConfiguration<Attendance>
{
    public void Configure(EntityTypeBuilder<Attendance> builder)
    {
        builder.Property(a => a.Version).IsConcurrencyToken().HasDefaultValue(1);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.RecordedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.LastModifiedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.Property(a => a.Status).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Method).HasMaxLength(20).IsRequired();
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Attendances_Version", "[Version] > 0");
            t.HasCheckConstraint("CK_Attendances_Status", "[Status] IN ('Present','Absent','Late','Excused','Unverified')");
            t.HasCheckConstraint("CK_Attendances_Method", "[Method] IN ('Manual','FaceRecognition','LegacyUnknown')");
        });
        builder.HasIndex(a => new { a.StudentId, a.AttendanceSessionId }).IsUnique();
        builder.HasOne(a => a.Student).WithMany().HasForeignKey(a => a.StudentId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(a => a.Course).WithMany().HasForeignKey(a => a.CourseId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(a => a.Session).WithMany()
            .HasForeignKey(a => new { a.AttendanceSessionId, a.CourseId })
            .HasPrincipalKey(s => new { s.AttendanceSessionId, s.CourseId }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(a => a.Enrollment).WithMany()
            .HasForeignKey(a => new { a.StudentId, a.CourseId }).OnDelete(DeleteBehavior.NoAction);
    }
}
