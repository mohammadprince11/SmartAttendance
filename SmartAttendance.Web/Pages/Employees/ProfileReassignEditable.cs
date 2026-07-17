using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Employees;

public partial class ProfileModel
{
    public List<ProfileReassignCompanyOption> ReassignCompanies { get; set; } = new();

    public List<ProfileReassignBranchOption> ReassignBranches { get; set; } = new();

    public List<ProfileReassignDepartmentOption> ReassignDepartments { get; set; } = new();

    public int CurrentReassignCompanyId { get; set; }

    public int CurrentReassignBranchId { get; set; }

    public int CurrentReassignDepartmentId { get; set; }

    private async Task LoadProfileReassignLookupsAsync()
    {
        var current = await LoadProfileReassignCurrentOrgAsync(Employee?.Id ?? Id);

        CurrentReassignCompanyId = current?.CompanyId ?? 0;
        CurrentReassignBranchId = current?.BranchId ?? 0;
        CurrentReassignDepartmentId = current?.DepartmentId ?? 0;

        ReassignCompanies = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT Id, Name
FROM Companies
WHERE IsActive = 1 OR Id = @CurrentCompanyId
ORDER BY Name;",
            command => HrmsDatabase.AddParameter(command, "@CurrentCompanyId", CurrentReassignCompanyId),
            reader => new ProfileReassignCompanyOption
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Name = HrmsDatabase.GetString(reader, "Name")
            });

        ReassignBranches = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT Id, Name, CompanyId
FROM Branches
WHERE IsActive = 1 OR CompanyId = @CurrentCompanyId OR Id = @CurrentBranchId
ORDER BY CompanyId, Name;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CurrentCompanyId", CurrentReassignCompanyId);
                HrmsDatabase.AddParameter(command, "@CurrentBranchId", CurrentReassignBranchId);
            },
            reader => new ProfileReassignBranchOption
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                CompanyId = HrmsDatabase.GetInt(reader, "CompanyId")
            });

        ReassignDepartments = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT Id, Name, CompanyId
FROM Departments
WHERE IsActive = 1 OR Id = @CurrentDepartmentId
ORDER BY CompanyId, Name;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@CurrentDepartmentId", CurrentReassignDepartmentId);
            },
            reader => new ProfileReassignDepartmentOption
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                CompanyId = HrmsDatabase.GetInt(reader, "CompanyId")
            });
    }

    public async Task<IActionResult> OnPostReassignFromModalV2Async(
        int id,
        int reassignCompanyId,
        int reassignBranchId,
        int reassignDepartmentId,
        string? reassignDate,
        string? reassignPosition,
        string? reassignReason,
        string? reassignNotes,
        string? confirmReassign)
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        await EmployeeLifecycleSchema.EnsureAsync(_dbContext);

        if (id <= 0)
        {
            TempData["ErrorMessage"] = "Employee was not selected.";
            return RedirectToPage("./Index");
        }

        if (!await HasEmployeeActionPermissionAsync(
                SmartAttendance.Application.Common.Security.PeoplePermissionCodes.Rehire,
                id))
        {
            return Forbid();
        }

        var employee = await LoadProfileReassignEmployeeV2Async(id);

        if (employee == null)
        {
            TempData["ErrorMessage"] = "Employee was not found.";
            return RedirectToPage("./Index");
        }

        if (employee.IsActive)
        {
            TempData["ErrorMessage"] = "Employee is already active.";
            return RedirectToPage(new { id });
        }

        if (!DateOnly.TryParse(reassignDate, out var reassignDateValue))
        {
            TempData["ErrorMessage"] = "Select the new joining date.";
            return RedirectToPage(new { id });
        }

        if (string.IsNullOrWhiteSpace(reassignReason))
        {
            TempData["ErrorMessage"] = "Enter the reassign reason.";
            return RedirectToPage(new { id });
        }

        var isConfirmed =
            string.Equals(confirmReassign, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(confirmReassign, "on", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(confirmReassign, "1", StringComparison.OrdinalIgnoreCase);

        if (!isConfirmed)
        {
            TempData["ErrorMessage"] = "Confirm the reassign action before saving.";
            return RedirectToPage(new { id });
        }

        var companyId = reassignCompanyId > 0 ? reassignCompanyId : employee.CompanyId;
        var branchId = reassignBranchId > 0 ? reassignBranchId : employee.BranchId;
        var departmentId = reassignDepartmentId > 0 ? reassignDepartmentId : employee.DepartmentId;

        var target = await LoadProfileReassignTargetOrgV2Async(companyId, branchId, departmentId);

        if (target == null)
        {
            TempData["ErrorMessage"] = "Select valid company, work location, and department.";
            return RedirectToPage(new { id });
        }

        var newPosition = string.IsNullOrWhiteSpace(reassignPosition)
            ? employee.Position
            : reassignPosition.Trim();

        var reason = reassignReason.Trim();
        var notes = BuildProfileReassignNotesV2(employee, target, newPosition, reassignNotes);
        var userName = User.Identity?.Name ?? "System";
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

        object? previousHireDateSql = employee.HireDate.HasValue
            ? employee.HireDate.Value.ToDateTime(TimeOnly.MinValue)
            : null;

        var reassignDateSql = reassignDateValue.ToDateTime(TimeOnly.MinValue);

        var oldValues =
            $"IsActive: {employee.IsActive}; HireDate: {DisplayDate(employee.HireDate)}; Company: {employee.CompanyName}; Branch: {employee.BranchName}; Department: {employee.DepartmentName}; Position: {employee.Position}; EmploymentStatus: {employee.EmploymentStatus}; ServiceEndDate: {DisplayDate(employee.ServiceEndDate)}";

        var newValues =
            $"IsActive: True; HireDate/ReassignDate: {reassignDateValue:yyyy-MM-dd}; Company: {target.CompanyName}; Branch: {target.BranchName}; Department: {target.DepartmentName}; Position: {newPosition}; EmploymentStatus: Active";

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            @"
INSERT INTO EmployeeRehires
(
    EmployeeId,
    EmployeeNo,
    EmployeeName,
    PreviousHireDate,
    RehireDate,
    PreviousEmploymentStatus,
    Reason,
    HrNotes,
    CreatedBy,
    IpAddress,
    CreatedAt
)
VALUES
(
    @EmployeeId,
    @EmployeeNo,
    @EmployeeName,
    @PreviousHireDate,
    @RehireDate,
    @PreviousEmploymentStatus,
    @Reason,
    @HrNotes,
    @CreatedBy,
    @IpAddress,
    GETDATE()
);

UPDATE Employees
SET
    IsActive = 1,
    HireDate = @RehireDate,
    Position = @Position,
    DepartmentId = @DepartmentId,
    EmploymentStatus = N'Active',
    ServiceEndDate = NULL,
    ServiceEndType = NULL,
    ServiceEndReason = NULL,
    ServiceEndNotes = NULL,
    ClearanceStatus = NULL,
    LastRehireDate = @RehireDate,
    RehireReason = @Reason,
    RehireNotes = @HrNotes,
    RehireCount = ISNULL(RehireCount, 0) + 1
WHERE Id = @EmployeeId;

IF OBJECT_ID('AuditLogs', 'U') IS NOT NULL
BEGIN
    INSERT INTO AuditLogs
    (
        EntityName,
        EntityId,
        Action,
        OldValues,
        NewValues,
        UserName,
        IpAddress
    )
    VALUES
    (
        'Employee',
        CAST(@EmployeeId AS nvarchar(80)),
        'Employee Reassigned From Profile Modal',
        @OldValues,
        @NewValues,
        @CreatedBy,
        @IpAddress
    );
END;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", id);
                HrmsDatabase.AddParameter(command, "@EmployeeNo", employee.EmployeeNo);
                HrmsDatabase.AddParameter(command, "@EmployeeName", employee.FullName);
                HrmsDatabase.AddParameter(command, "@PreviousHireDate", previousHireDateSql);
                HrmsDatabase.AddParameter(command, "@RehireDate", reassignDateSql);
                HrmsDatabase.AddParameter(command, "@PreviousEmploymentStatus", employee.EmploymentStatus);
                HrmsDatabase.AddParameter(command, "@DepartmentId", target.DepartmentId);
                HrmsDatabase.AddParameter(command, "@Position", newPosition);
                HrmsDatabase.AddParameter(command, "@Reason", reason);
                HrmsDatabase.AddParameter(command, "@HrNotes", string.IsNullOrWhiteSpace(notes) ? null : notes);
                HrmsDatabase.AddParameter(command, "@CreatedBy", userName);
                HrmsDatabase.AddParameter(command, "@IpAddress", ipAddress);
                HrmsDatabase.AddParameter(command, "@OldValues", oldValues);
                HrmsDatabase.AddParameter(command, "@NewValues", newValues);
            });

        TempData["SuccessMessage"] = "Employee reassigned successfully and saved to lifecycle history.";
        return RedirectToPage(new { id });
    }

    private async Task<ProfileReassignCurrentOrgRow?> LoadProfileReassignCurrentOrgAsync(int employeeId)
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT TOP 1
    e.DepartmentId,
    e.BranchId AS BranchId,
    ISNULL(b.CompanyId, 0) AS CompanyId
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON e.BranchId = b.Id
WHERE e.Id = @EmployeeId;",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new ProfileReassignCurrentOrgRow
            {
                DepartmentId = HrmsDatabase.GetInt(reader, "DepartmentId"),
                BranchId = HrmsDatabase.GetInt(reader, "BranchId"),
                CompanyId = HrmsDatabase.GetInt(reader, "CompanyId")
            });

        return rows.FirstOrDefault();
    }

    private async Task<ProfileReassignEmployeeV2Row?> LoadProfileReassignEmployeeV2Async(int employeeId)
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT TOP 1
    e.Id,
    e.EmployeeNo,
    e.FullName,
    e.HireDate,
    e.IsActive,
    e.DepartmentId,
    e.BranchId AS BranchId,
    ISNULL(b.CompanyId, 0) AS CompanyId,
    ISNULL(e.Position, '') AS Position,
    ISNULL(e.EmploymentStatus, '') AS EmploymentStatus,
    e.ServiceEndDate,
    ISNULL(e.ServiceEndReason, '') AS ServiceEndReason,
    ISNULL(d.Name, '') AS DepartmentName,
    ISNULL(b.Name, '') AS BranchName,
    ISNULL(c.Name, '') AS CompanyName
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON e.BranchId = b.Id
LEFT JOIN Companies c ON b.CompanyId = c.Id
WHERE e.Id = @EmployeeId;",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new ProfileReassignEmployeeV2Row
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                HireDate = HrmsDatabase.GetDateOnly(reader, "HireDate"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                DepartmentId = HrmsDatabase.GetInt(reader, "DepartmentId"),
                BranchId = HrmsDatabase.GetInt(reader, "BranchId"),
                CompanyId = HrmsDatabase.GetInt(reader, "CompanyId"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                EmploymentStatus = HrmsDatabase.GetString(reader, "EmploymentStatus"),
                ServiceEndDate = HrmsDatabase.GetDateOnly(reader, "ServiceEndDate"),
                ServiceEndReason = HrmsDatabase.GetString(reader, "ServiceEndReason"),
                DepartmentName = HrmsDatabase.GetString(reader, "DepartmentName"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName"),
                CompanyName = HrmsDatabase.GetString(reader, "CompanyName")
            });

        return rows.FirstOrDefault();
    }

    private async Task<ProfileReassignTargetOrgV2Row?> LoadProfileReassignTargetOrgV2Async(int companyId, int branchId, int departmentId)
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT TOP 1
    d.Id AS DepartmentId,
    d.Name AS DepartmentName,
    b.Id AS BranchId,
    b.Name AS BranchName,
    c.Id AS CompanyId,
    c.Name AS CompanyName
FROM Departments d
INNER JOIN Branches b
    ON b.Id = @BranchId
   AND b.CompanyId = d.CompanyId
INNER JOIN Companies c ON c.Id = d.CompanyId
WHERE d.Id = @DepartmentId
  AND b.Id = @BranchId
  AND c.Id = @CompanyId;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@DepartmentId", departmentId);
                HrmsDatabase.AddParameter(command, "@BranchId", branchId);
                HrmsDatabase.AddParameter(command, "@CompanyId", companyId);
            },
            reader => new ProfileReassignTargetOrgV2Row
            {
                DepartmentId = HrmsDatabase.GetInt(reader, "DepartmentId"),
                DepartmentName = HrmsDatabase.GetString(reader, "DepartmentName"),
                BranchId = HrmsDatabase.GetInt(reader, "BranchId"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName"),
                CompanyId = HrmsDatabase.GetInt(reader, "CompanyId"),
                CompanyName = HrmsDatabase.GetString(reader, "CompanyName")
            });

        return rows.FirstOrDefault();
    }

    private static string BuildProfileReassignNotesV2(ProfileReassignEmployeeV2Row employee, ProfileReassignTargetOrgV2Row target, string newPosition, string? hrNotes)
    {
        var items = new List<string>();

        if (!string.Equals(employee.CompanyName?.Trim(), target.CompanyName?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            items.Add($"Company changed from '{employee.CompanyName}' to '{target.CompanyName}'.");
        }

        if (!string.Equals(employee.BranchName?.Trim(), target.BranchName?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            items.Add($"Work location changed from '{employee.BranchName}' to '{target.BranchName}'.");
        }

        if (!string.Equals(employee.DepartmentName?.Trim(), target.DepartmentName?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            items.Add($"Department changed from '{employee.DepartmentName}' to '{target.DepartmentName}'.");
        }

        if (!string.Equals(employee.Position?.Trim(), newPosition?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            items.Add($"Position changed from '{employee.Position}' to '{newPosition}'.");
        }

        if (!string.IsNullOrWhiteSpace(hrNotes))
        {
            items.Add(hrNotes.Trim());
        }

        return string.Join(" | ", items);
    }

    public class ProfileReassignCompanyOption
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class ProfileReassignBranchOption
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class ProfileReassignDepartmentOption
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private class ProfileReassignCurrentOrgRow
    {
        public int CompanyId { get; set; }

        public int BranchId { get; set; }

        public int DepartmentId { get; set; }
    }

    private class ProfileReassignTargetOrgV2Row
    {
        public int CompanyId { get; set; }

        public string CompanyName { get; set; } = string.Empty;

        public int BranchId { get; set; }

        public string BranchName { get; set; } = string.Empty;

        public int DepartmentId { get; set; }

        public string DepartmentName { get; set; } = string.Empty;
    }

    private class ProfileReassignEmployeeV2Row
    {
        public int Id { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public DateOnly? HireDate { get; set; }

        public bool IsActive { get; set; }

        public int CompanyId { get; set; }

        public string CompanyName { get; set; } = string.Empty;

        public int BranchId { get; set; }

        public string BranchName { get; set; } = string.Empty;

        public int DepartmentId { get; set; }

        public string DepartmentName { get; set; } = string.Empty;

        public string Position { get; set; } = string.Empty;

        public string EmploymentStatus { get; set; } = string.Empty;

        public DateOnly? ServiceEndDate { get; set; }

        public string ServiceEndReason { get; set; } = string.Empty;
    }
}