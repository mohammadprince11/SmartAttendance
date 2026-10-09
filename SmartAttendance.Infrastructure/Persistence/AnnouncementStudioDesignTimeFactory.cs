using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartAttendance.Infrastructure.Persistence;

/// <summary>Schema generation only: never loads production configuration or opens a connection.</summary>
public sealed class AnnouncementStudioDesignTimeFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlServer("Server=localhost;Database=ZynoraDesignTimeOnly;Integrated Security=True;TrustServerCertificate=True")
        .Options);
}
