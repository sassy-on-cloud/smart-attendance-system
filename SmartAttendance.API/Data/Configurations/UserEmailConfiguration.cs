using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartAttendance.API.Models;

namespace SmartAttendance.API.Data.Configurations;

public sealed class UserEmailConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(u => u.NormalizedEmail).HasMaxLength(254);
        builder.HasIndex(u => u.NormalizedEmail).IsUnique().HasFilter("[NormalizedEmail] IS NOT NULL");
        // Blank historical identities remain inert, not silently merged. New registrations require valid email.
        builder.ToTable(t => t.HasCheckConstraint("CK_Users_NormalizedEmail", """
            ([NormalizedEmail] IS NULL AND LTRIM(RTRIM([Email])) = '') OR
            ([NormalizedEmail] IS NOT NULL AND LTRIM(RTRIM([Email])) <> '' AND [NormalizedEmail] <> '')
            """));
    }
}
