using SmartAttendance.Application.Common.Security;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Security;

public static class PeopleDataScopeQueryExtensions
{
    public static IQueryable<Employee> ApplyPeopleDataScope(
        this IQueryable<Employee> query,
        PeopleDataScope scope)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(scope);

        if (scope.IsDeniedAll)
        {
            return query.Where(_ => false);
        }

        var allowedCompanyIds = scope.AllowedCompanyIds.ToArray();
        var allowedBranchIds = scope.AllowedBranchIds.ToArray();
        var allowedDepartmentIds = scope.AllowedDepartmentIds.ToArray();
        var allowedEmployeeIds = scope.AllowedEmployeeIds.ToArray();
        var deniedCompanyIds = scope.DeniedCompanyIds.ToArray();
        var deniedBranchIds = scope.DeniedBranchIds.ToArray();
        var deniedDepartmentIds = scope.DeniedDepartmentIds.ToArray();
        var deniedEmployeeIds = scope.DeniedEmployeeIds.ToArray();
        var selfEmployeeId = scope.SelfEmployeeId;

        if (!scope.IsUnrestricted)
        {
            if (!scope.HasAnyAllowance)
            {
                return query.Where(_ => false);
            }

            query = query.Where(employee =>
                allowedCompanyIds.Contains(employee.Branch.CompanyId) ||
                allowedBranchIds.Contains(employee.BranchId) ||
                allowedDepartmentIds.Contains(employee.DepartmentId) ||
                allowedEmployeeIds.Contains(employee.Id) ||
                (scope.AllowSelf &&
                 selfEmployeeId.HasValue &&
                 employee.Id == selfEmployeeId.Value));
        }

        return query.Where(employee =>
            !deniedCompanyIds.Contains(employee.Branch.CompanyId) &&
            !deniedBranchIds.Contains(employee.BranchId) &&
            !deniedDepartmentIds.Contains(employee.DepartmentId) &&
            !deniedEmployeeIds.Contains(employee.Id) &&
            !(scope.DenySelf &&
              selfEmployeeId.HasValue &&
              employee.Id == selfEmployeeId.Value));
    }
}
