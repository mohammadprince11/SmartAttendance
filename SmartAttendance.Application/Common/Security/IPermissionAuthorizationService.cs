namespace SmartAttendance.Application.Common.Security;

public interface IPermissionAuthorizationService
{
    Task<bool> HasDirectGrantAsync(
        int systemUserId,
        string permissionCode,
        CancellationToken cancellationToken = default);
}
