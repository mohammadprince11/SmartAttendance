namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>One company-scoped audience predicate for poll display and voting.
/// Numeric targets are identifiers; legacy nonnumeric names/codes use exact equality only.</summary>
public static class PollAudience
{
    // p is the poll alias. Never interpolate request values into this SQL.
    public const string SqlPredicate = """
EXISTS (
    SELECT 1 FROM Employees pollEmployee
    LEFT JOIN Departments pollDepartment ON pollDepartment.Id = pollEmployee.DepartmentId
        AND pollDepartment.CompanyId = pollEmployee.CompanyId
    LEFT JOIN Branches pollBranch ON pollBranch.Id = pollEmployee.BranchId
        AND pollBranch.CompanyId = pollEmployee.CompanyId AND ISNULL(pollBranch.IsDeleted, 0) = 0
    WHERE pollEmployee.Id = @EmployeeId AND ISNULL(pollEmployee.IsDeleted, 0) = 0
      AND (p.CompanyId IS NULL OR p.CompanyId = pollEmployee.CompanyId)
      AND (
          p.TargetType IS NULL OR p.TargetType = N'All'
          OR (p.TargetType = N'Employee' AND EXISTS (
              SELECT 1 FROM STRING_SPLIT(p.TargetValue, N',') pollTarget
              WHERE TRY_CONVERT(int, TRIM(pollTarget.value)) = pollEmployee.Id
                 OR (TRY_CONVERT(int, TRIM(pollTarget.value)) IS NULL
                     AND TRIM(pollTarget.value) = NULLIF(pollEmployee.EmployeeNo, N''))))
          OR (p.TargetType = N'Department' AND (
              TRY_CONVERT(int, p.TargetValue) = pollDepartment.Id
              OR (TRY_CONVERT(int, p.TargetValue) IS NULL
                  AND p.TargetValue = NULLIF(pollDepartment.Name, N''))))
          OR (p.TargetType = N'Branch' AND (
              TRY_CONVERT(int, p.TargetValue) = pollBranch.Id
              OR (TRY_CONVERT(int, p.TargetValue) IS NULL
                  AND p.TargetValue = NULLIF(pollBranch.Name, N''))))
      )
)
""";
}
