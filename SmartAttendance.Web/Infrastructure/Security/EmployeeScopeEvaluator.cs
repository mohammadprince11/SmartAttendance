namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// Pure evaluator for the Employees data scope of an Access Role. Given the
/// acting user's own org context, decides whether a specific employee record is
/// in scope. Deterministic and side-effect free so it is fully unit-testable and
/// safe to compose (as an AND) with the existing People data-scope system.
///
/// It only ever restricts: "All" (and the unknown default) admit everything, so
/// wiring it as an additional constraint can never widen access.
/// </summary>
public static class EmployeeScopeEvaluator
{
    /// <summary>The acting user's own organisational anchors.</summary>
    public readonly record struct ActingUser(int BranchId, int DepartmentId, int EmployeeId);

    /// <summary>The employee record being tested.</summary>
    public readonly record struct EmployeeRef(int Id, int BranchId, int DepartmentId);

    public static bool IsInScope(string? scope, ActingUser user, EmployeeRef employee) =>
        (scope ?? "All") switch
        {
            "Self" => employee.Id == user.EmployeeId && user.EmployeeId > 0,
            "OwnDepartment" => employee.DepartmentId == user.DepartmentId && user.DepartmentId > 0,
            "OwnBranch" => employee.BranchId == user.BranchId && user.BranchId > 0,
            // Company scope is enforced at the list/company level elsewhere; here
            // it does not narrow within an already company-scoped set.
            "OwnCompany" => true,
            "All" => true,
            // Unknown scope must never silently deny.
            _ => true,
        };
}
