using SmartAttendance.Application.Common.Security;

namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// Unifies the Access Roles data scope with the existing People scope model by
/// translating an AccessRoles Employees scope (All / OwnCompany / OwnBranch /
/// OwnDepartment / Self) into a <see cref="PeopleDataScope"/>, expressed in the
/// acting user's own organisational anchors. The result is composed (AND-ed)
/// with the rules-based scope at the point of use, so the two systems yield a
/// single effective scope instead of competing.
///
/// Pure and deterministic. "None"/unknown and empty anchors resolve to
/// Unrestricted so an unconfigured user is never silently denied.
/// </summary>
public static class AccessRoleScopeTranslator
{
    public readonly record struct UserAnchors(int CompanyId, int BranchId, int DepartmentId, int? EmployeeId);

    public static PeopleDataScope ToPeopleDataScope(string? scope, UserAnchors anchors) =>
        (scope ?? "All") switch
        {
            "OwnCompany" => anchors.CompanyId > 0
                ? new PeopleDataScope { AllowedCompanyIds = new[] { anchors.CompanyId }, SelfEmployeeId = anchors.EmployeeId }
                : PeopleDataScope.Unrestricted(anchors.EmployeeId),

            "OwnBranch" => anchors.BranchId > 0
                ? new PeopleDataScope { AllowedBranchIds = new[] { anchors.BranchId }, SelfEmployeeId = anchors.EmployeeId }
                : PeopleDataScope.Unrestricted(anchors.EmployeeId),

            "OwnDepartment" => anchors.DepartmentId > 0
                ? new PeopleDataScope { AllowedDepartmentIds = new[] { anchors.DepartmentId }, SelfEmployeeId = anchors.EmployeeId }
                : PeopleDataScope.Unrestricted(anchors.EmployeeId),

            "Self" => anchors.EmployeeId is > 0
                ? new PeopleDataScope { AllowSelf = true, SelfEmployeeId = anchors.EmployeeId }
                : PeopleDataScope.Unrestricted(anchors.EmployeeId),

            // "All", "None", and any unknown value impose no restriction.
            _ => PeopleDataScope.Unrestricted(anchors.EmployeeId),
        };
}
