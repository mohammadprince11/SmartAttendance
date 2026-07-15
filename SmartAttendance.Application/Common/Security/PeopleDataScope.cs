namespace SmartAttendance.Application.Common.Security;

public sealed class PeopleDataScope
{
    public bool IsUnrestricted { get; init; }

    public bool IsDeniedAll { get; init; }

    public int? SelfEmployeeId { get; init; }

    public bool AllowSelf { get; init; }

    public bool DenySelf { get; init; }

    public IReadOnlyList<int> AllowedCompanyIds { get; init; } = Array.Empty<int>();

    public IReadOnlyList<int> AllowedBranchIds { get; init; } = Array.Empty<int>();

    public IReadOnlyList<int> AllowedDepartmentIds { get; init; } = Array.Empty<int>();

    public IReadOnlyList<int> AllowedEmployeeIds { get; init; } = Array.Empty<int>();

    public IReadOnlyList<int> DeniedCompanyIds { get; init; } = Array.Empty<int>();

    public IReadOnlyList<int> DeniedBranchIds { get; init; } = Array.Empty<int>();

    public IReadOnlyList<int> DeniedDepartmentIds { get; init; } = Array.Empty<int>();

    public IReadOnlyList<int> DeniedEmployeeIds { get; init; } = Array.Empty<int>();

    public bool HasAnyAllowance =>
        IsUnrestricted ||
        AllowedCompanyIds.Count > 0 ||
        AllowedBranchIds.Count > 0 ||
        AllowedDepartmentIds.Count > 0 ||
        AllowedEmployeeIds.Count > 0 ||
        (AllowSelf && SelfEmployeeId.HasValue);

    public bool HasAnyDenial =>
        IsDeniedAll ||
        DeniedCompanyIds.Count > 0 ||
        DeniedBranchIds.Count > 0 ||
        DeniedDepartmentIds.Count > 0 ||
        DeniedEmployeeIds.Count > 0 ||
        (DenySelf && SelfEmployeeId.HasValue);

    public bool AllowsEmployee(
        int employeeId,
        int companyId,
        int branchId,
        int departmentId)
    {
        if (employeeId <= 0 || IsDeniedAll)
        {
            return false;
        }

        var denied =
            DeniedCompanyIds.Contains(companyId) ||
            DeniedBranchIds.Contains(branchId) ||
            DeniedDepartmentIds.Contains(departmentId) ||
            DeniedEmployeeIds.Contains(employeeId) ||
            (DenySelf && SelfEmployeeId == employeeId);

        if (denied)
        {
            return false;
        }

        if (IsUnrestricted)
        {
            return true;
        }

        return AllowedCompanyIds.Contains(companyId) ||
               AllowedBranchIds.Contains(branchId) ||
               AllowedDepartmentIds.Contains(departmentId) ||
               AllowedEmployeeIds.Contains(employeeId) ||
               (AllowSelf && SelfEmployeeId == employeeId);
    }

    public static PeopleDataScope Empty(int? selfEmployeeId = null) => new()
    {
        SelfEmployeeId = selfEmployeeId
    };

    public static PeopleDataScope Unrestricted(int? selfEmployeeId = null) => new()
    {
        IsUnrestricted = true,
        SelfEmployeeId = selfEmployeeId
    };
}
