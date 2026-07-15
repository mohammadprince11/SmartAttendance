namespace SmartAttendance.Application.Common.Security;

public interface IPermissionAuthorizationService
{
    Task<bool> HasDirectGrantAsync(
        int systemUserId,
        string permissionCode,
        CancellationToken cancellationToken = default);

    Task<bool> HasPermissionAsync(
        int systemUserId,
        string permissionCode,
        bool compatibilityAllowed = false,
        CancellationToken cancellationToken = default);

    Task<bool> HasGlobalPermissionAsync(
        int systemUserId,
        string permissionCode,
        bool compatibilityAllowed = false,
        CancellationToken cancellationToken = default);

    Task<PeopleDataScope> GetPeopleDataScopeAsync(
        int systemUserId,
        string permissionCode,
        bool compatibilityUnrestricted = false,
        CancellationToken cancellationToken = default);

    Task<bool> CanAccessEmployeeAsync(
        int systemUserId,
        string permissionCode,
        int employeeId,
        bool compatibilityAllowed = false,
        CancellationToken cancellationToken = default);
}
