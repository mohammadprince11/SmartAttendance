using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Infrastructure.Services;

public sealed class LoginIdentityService : ILoginIdentityService
{
    private readonly ApplicationDbContext _dbContext;

    public LoginIdentityService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int?> EnsureSystemUserAsync(
        LoginIdentityRequest request,
        CancellationToken cancellationToken = default)
    {
        var userName = request.UserName.Trim();

        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        SystemUser? systemUser = null;

        if (request.EmployeeId.HasValue && request.EmployeeId.Value > 0)
        {
            systemUser = await _dbContext.SystemUsers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    x => x.EmployeeId == request.EmployeeId.Value,
                    cancellationToken);
        }

        systemUser ??= await _dbContext.SystemUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.UserName == userName,
                cancellationToken);

        if (systemUser != null &&
            request.EmployeeId.HasValue &&
            request.EmployeeId.Value > 0 &&
            systemUser.EmployeeId.HasValue &&
            systemUser.EmployeeId.Value != request.EmployeeId.Value)
        {
            throw new InvalidOperationException(
                "The login username is linked to a different employee identity.");
        }

        if (systemUser == null)
        {
            systemUser = new SystemUser
            {
                EmployeeId = request.EmployeeId,
                FullName = ResolveDisplayName(request.DisplayName, userName),
                UserName = userName,
                Role = MapCompatibilityRole(request.CompatibilityRole),
                IsActive = request.IsActive,
                Notes = "Linked automatically from the application login identity."
            };

            await _dbContext.SystemUsers.AddAsync(systemUser, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return systemUser.Id;
        }

        var changed = false;
        var displayName = ResolveDisplayName(request.DisplayName, systemUser.FullName);

        if (systemUser.IsDeleted)
        {
            systemUser.IsDeleted = false;
            changed = true;
        }

        if (!string.Equals(systemUser.FullName, displayName, StringComparison.Ordinal))
        {
            systemUser.FullName = displayName;
            changed = true;
        }

        if (systemUser.IsActive != request.IsActive)
        {
            systemUser.IsActive = request.IsActive;
            changed = true;
        }

        if (!systemUser.EmployeeId.HasValue && request.EmployeeId.HasValue && request.EmployeeId.Value > 0)
        {
            systemUser.EmployeeId = request.EmployeeId;
            changed = true;
        }

        if (changed)
        {
            systemUser.UpdatedAt = DateTime.UtcNow;
            _dbContext.SystemUsers.Update(systemUser);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return systemUser.Id;
    }

    private static string ResolveDisplayName(string displayName, string fallback)
    {
        return string.IsNullOrWhiteSpace(displayName)
            ? fallback.Trim()
            : displayName.Trim();
    }

    private static SystemUserRole MapCompatibilityRole(string compatibilityRole)
    {
        if (compatibilityRole.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return SystemUserRole.Admin;
        }

        if (compatibilityRole.Equals("HR Manager", StringComparison.OrdinalIgnoreCase) ||
            compatibilityRole.Equals("HR Officer", StringComparison.OrdinalIgnoreCase))
        {
            return SystemUserRole.HR;
        }

        return SystemUserRole.Viewer;
    }
}
