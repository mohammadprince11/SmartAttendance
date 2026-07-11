using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

public static class PositionSchema
{
    public static Task EnsureAsync(ApplicationDbContext dbContext)
    {
        return Task.CompletedTask;
    }
}
