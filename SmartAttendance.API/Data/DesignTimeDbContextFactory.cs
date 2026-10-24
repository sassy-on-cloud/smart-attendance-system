using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartAttendance.API.Data;

// Migration generation/scripts never load local credentials or target the project database.
// Apply reviewed SQL separately, with explicit approval; this factory is intentionally offline.
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer("Server=127.0.0.1,1;Database=SmartAttendanceDesignTimeOnly;Integrated Security=True;Connect Timeout=1")
        .Options);
}
