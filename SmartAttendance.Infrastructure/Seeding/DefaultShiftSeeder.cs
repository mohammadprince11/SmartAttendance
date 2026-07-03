using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Infrastructure.Seeding;

public static class DefaultShiftSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var defaultShifts = new List<Shift>
        {
            new()
            {
                Code = "MOR",
                Name = "Morning Shift",
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(17, 0),
                WorkingHours = 9,
                GraceInMinutes = 15,
                GraceOutMinutes = 0,
                IsNightShift = false,
                IsActive = true
            },
            new()
            {
                Code = "MID",
                Name = "Middle Shift",
                StartTime = new TimeOnly(12, 0),
                EndTime = new TimeOnly(21, 0),
                WorkingHours = 9,
                GraceInMinutes = 15,
                GraceOutMinutes = 0,
                IsNightShift = false,
                IsActive = true
            },
            new()
            {
                Code = "EVE",
                Name = "Evening Shift",
                StartTime = new TimeOnly(16, 0),
                EndTime = new TimeOnly(1, 0),
                WorkingHours = 9,
                GraceInMinutes = 15,
                GraceOutMinutes = 0,
                IsNightShift = true,
                IsActive = true
            },
            new()
            {
                Code = "NIG",
                Name = "Night Shift",
                StartTime = new TimeOnly(22, 0),
                EndTime = new TimeOnly(7, 0),
                WorkingHours = 9,
                GraceInMinutes = 15,
                GraceOutMinutes = 0,
                IsNightShift = true,
                IsActive = true
            },
            new()
            {
                Code = "HQ7",
                Name = "HQ Office Shift",
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(15, 0),
                WorkingHours = 7,
                GraceInMinutes = 15,
                GraceOutMinutes = 0,
                IsNightShift = false,
                IsActive = true
            },
            new()
            {
                Code = "PT4",
                Name = "Part Time 4 Hours",
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(12, 0),
                WorkingHours = 4,
                GraceInMinutes = 10,
                GraceOutMinutes = 0,
                IsNightShift = false,
                IsActive = true
            },
            new()
            {
                Code = "SPL",
                Name = "Split Shift",
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(17, 0),
                WorkingHours = 8,
                GraceInMinutes = 15,
                GraceOutMinutes = 0,
                IsNightShift = false,
                IsActive = true
            }
        };

        foreach (var shift in defaultShifts)
        {
            var exists = await dbContext.Shifts
                .AnyAsync(x => x.Code.ToLower() == shift.Code.ToLower());

            if (!exists)
            {
                await dbContext.Shifts.AddAsync(shift);
            }
        }

        await dbContext.SaveChangesAsync();
    }
}
