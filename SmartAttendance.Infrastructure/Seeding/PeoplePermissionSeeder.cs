using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Infrastructure.Seeding;

public static class PeoplePermissionSeeder
{
    public static async Task SeedAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var codes = PeoplePermissionCodes.Definitions
            .Select(x => x.Code)
            .ToList();

        var existingPermissions = await dbContext.Permissions
            .IgnoreQueryFilters()
            .Where(x => codes.Contains(x.Code))
            .ToListAsync(cancellationToken);

        var existing = existingPermissions
            .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

        var changed = false;

        foreach (var definition in PeoplePermissionCodes.Definitions)
        {
            if (!existing.TryGetValue(definition.Code, out var permission))
            {
                await dbContext.Permissions.AddAsync(
                    new Permission
                    {
                        Module = definition.Module,
                        Code = definition.Code,
                        Name = definition.Name,
                        Description = definition.Description,
                        DisplayOrder = definition.DisplayOrder,
                        IsActive = true,
                        CreatedBy = "PeoplePermissionSeeder"
                    },
                    cancellationToken);

                changed = true;
                continue;
            }

            if (permission.Module != definition.Module ||
                permission.Name != definition.Name ||
                permission.Description != definition.Description ||
                permission.DisplayOrder != definition.DisplayOrder ||
                !permission.IsActive ||
                permission.IsDeleted)
            {
                permission.Module = definition.Module;
                permission.Name = definition.Name;
                permission.Description = definition.Description;
                permission.DisplayOrder = definition.DisplayOrder;
                permission.IsActive = true;
                permission.IsDeleted = false;
                permission.UpdatedAt = DateTime.UtcNow;
                permission.UpdatedBy = "PeoplePermissionSeeder";
                changed = true;
            }
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
