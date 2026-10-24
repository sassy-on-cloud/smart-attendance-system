using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.API.Models;

namespace SmartAttendance.API.Data.Configurations;

public class AttendanceHistoryConfiguration : IEntityTypeConfiguration<AttendanceHistory>
{
    public void Configure(EntityTypeBuilder<AttendanceHistory> builder)
    {
        builder.HasIndex(h => new { h.AttendanceId, h.Version }).IsUnique();
        builder.HasOne(h => h.Attendance).WithMany().HasForeignKey(h => h.AttendanceId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(h => h.Actor).WithMany().HasForeignKey(h => h.ActorUserId).OnDelete(DeleteBehavior.NoAction);
        builder.Property(h => h.Reason).HasMaxLength(500).IsRequired();
        builder.Property(h => h.ChangeType).HasMaxLength(20).IsRequired();
        builder.Property(h => h.Status).HasMaxLength(20).IsRequired();
        builder.Property(h => h.Method).HasMaxLength(20).IsRequired();
        builder.Property(h => h.PreviousStatus).HasMaxLength(20);
        builder.Property(h => h.PreviousMethod).HasMaxLength(20);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_AttendanceHistory_Version", "[Version] > 0");
            t.HasCheckConstraint("CK_AttendanceHistory_ChangeType", "[ChangeType] IN ('Created','Corrected')");
            t.HasCheckConstraint("CK_AttendanceHistory_Reason", "[Reason] <> ''");
        });
    }
}

public class AttendanceSessionHistoryConfiguration : IEntityTypeConfiguration<AttendanceSessionHistory>
{
    public void Configure(EntityTypeBuilder<AttendanceSessionHistory> builder)
    {
        builder.HasIndex(h => new { h.AttendanceSessionId, h.Version }).IsUnique();
        builder.HasOne(h => h.Session).WithMany().HasForeignKey(h => h.AttendanceSessionId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(h => h.Actor).WithMany().HasForeignKey(h => h.ActorUserId).OnDelete(DeleteBehavior.NoAction);
        builder.Property(h => h.Reason).HasMaxLength(500).IsRequired();
        builder.Property(h => h.State).HasMaxLength(20).IsRequired();
        builder.Property(h => h.PreviousState).HasMaxLength(20);
        builder.ToTable(t => t.HasCheckConstraint("CK_AttendanceSessionHistory_Version", "[Version] > 0"));
    }
}
