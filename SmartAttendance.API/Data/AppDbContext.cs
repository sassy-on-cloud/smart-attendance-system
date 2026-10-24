using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Models;
using SmartAttendance.API.Security;

namespace SmartAttendance.API.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }

        public DbSet<Student> Students { get; set; }

        public DbSet<Course> Courses { get; set; }

        public DbSet<Attendance> Attendances { get; set; }
        public DbSet<CourseEnrollment> CourseEnrollments { get; set; }
        public DbSet<TeacherCourseAssignment> TeacherCourseAssignments { get; set; }
        public DbSet<AttendanceSession> AttendanceSessions { get; set; }
        public DbSet<AttendanceHistory> AttendanceHistory { get; set; }
        public DbSet<AttendanceSessionHistory> AttendanceSessionHistory { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
            modelBuilder.Entity<User>().Property(u => u.NormalizedEmail).UseCollation(
                Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer" ? "Latin1_General_100_CI_AS" : "NOCASE");
        }

        private void NormalizeUserEmails()
        {
            foreach (var entry in ChangeTracker.Entries<User>().Where(e => e.State is EntityState.Added or EntityState.Modified))
                entry.Entity.NormalizedEmail = EmailIdentity.Normalize(entry.Entity.Email);
        }
        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            NormalizeUserEmails();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            NormalizeUserEmails();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }
}