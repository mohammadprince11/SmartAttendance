using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Infrastructure.Services;

public sealed class PermissionAuthorizationService : IPermissionAuthorizationService
{
    private readonly ApplicationDbContext _dbContext;

    public PermissionAuthorizationService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> HasDirectGrantAsync(
        int systemUserId,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        if (systemUserId <= 0 || string.IsNullOrWhiteSpace(permissionCode))
        {
            return false;
        }

        var normalizedCode = permissionCode.Trim();

        return await _dbContext.SystemUserPermissions
            .AsNoTracking()
            .AnyAsync(
                assignment =>
                    assignment.SystemUserId == systemUserId &&
                    assignment.SystemUser.IsActive &&
                    assignment.Permission.IsActive &&
                    assignment.Permission.Code == normalizedCode,
                cancellationToken);
    }
}
