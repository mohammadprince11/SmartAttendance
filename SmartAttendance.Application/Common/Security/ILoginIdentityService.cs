namespace SmartAttendance.Application.Common.Security;

public interface ILoginIdentityService
{
    Task<int?> EnsureSystemUserAsync(
        LoginIdentityRequest request,
        CancellationToken cancellationToken = default);
}
