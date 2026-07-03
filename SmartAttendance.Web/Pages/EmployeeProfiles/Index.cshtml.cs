using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.EmployeeProfiles;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty]
    public ProfileInput Input { get; set; } = new();

    public List<EmployeeProfileRow> Employees { get; set; } = new();

    public List<EmployeeOption> Managers { get; set; } = new();

    public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        await LoadEmployeesAsync();
        await LoadManagersAsync();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
UPDATE Employees
SET
    Position = @Position,
    Gender = @Gender,
    Nationality = @Nationality,
    Country = @Country,
    ContractType = @ContractType,
    ContractEndDate = @ContractEndDate,
    EmploymentStatus = @EmploymentStatus,
    DirectManagerId = @DirectManagerId
WHERE Id = @Id;

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('Employee', CAST(@Id AS nvarchar(80)), 'Update Extended Profile', @NewValues, @UserName, @IpAddress);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", Input.Id);
                HrmsDatabase.AddParameter(command, "@Position", Input.Position);
                HrmsDatabase.AddParameter(command, "@Gender", Input.Gender);
                HrmsDatabase.AddParameter(command, "@Nationality", Input.Nationality);
                HrmsDatabase.AddParameter(command, "@Country", Input.Country);
                HrmsDatabase.AddParameter(command, "@ContractType", Input.ContractType);
                HrmsDatabase.AddParameter(command, "@ContractEndDate", Input.ContractEndDate);
                HrmsDatabase.AddParameter(command, "@EmploymentStatus", Input.EmploymentStatus);
                HrmsDatabase.AddParameter(command, "@DirectManagerId", Input.DirectManagerId);
                HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(
                    ("Position", Input.Position),
                    ("Gender", Input.Gender),
                    ("Nationality", Input.Nationality),
                    ("Country", Input.Country),
                    ("ContractType", Input.ContractType),
                    ("ContractEndDate", Input.ContractEndDate),
                    ("EmploymentStatus", Input.EmploymentStatus),
                    ("DirectManagerId", Input.DirectManagerId)));
                HrmsDatabase.AddParameter(command, "@UserName", "HR");
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });

        Message = "تم تحديث ملف الموظف.";
        await LoadEmployeesAsync();
        await LoadManagersAsync();

        return Page();
    }

    private async Task LoadManagersAsync()
    {
        Managers = await HrmsDatabase.QueryAsync(
            _dbContext,
            "SELECT TOP 1000 Id, EmployeeNo, FullName FROM Employees WHERE IsActive = 1 ORDER BY EmployeeNo",
            null,
            reader => new EmployeeOption
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Text = $"{HrmsDatabase.GetString(reader, "EmployeeNo")} - {HrmsDatabase.GetString(reader, "FullName")}"
            });
    }

    private async Task LoadEmployeesAsync()
    {
        var sql = """
SELECT TOP 200
    e.Id,
    e.EmployeeNo,
    e.FullName,
    ISNULL(b.Name, '') AS Branch,
    ISNULL(d.Name, '') AS Department,
    ISNULL(e.Position, '') AS Position,
    ISNULL(e.Gender, '') AS Gender,
    ISNULL(e.Nationality, '') AS Nationality,
    ISNULL(e.Country, '') AS Country,
    ISNULL(e.ContractType, '') AS ContractType,
    e.ContractEndDate,
    ISNULL(e.EmploymentStatus, '') AS EmploymentStatus,
    e.DirectManagerId,
    ISNULL(m.FullName, '') AS DirectManager
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON d.BranchId = b.Id
LEFT JOIN Employees m ON e.DirectManagerId = m.Id
WHERE
    (@Search IS NULL OR @Search = ''
     OR e.EmployeeNo LIKE '%' + @Search + '%'
     OR e.FullName LIKE '%' + @Search + '%'
     OR ISNULL(e.Position, '') LIKE '%' + @Search + '%'
     OR ISNULL(d.Name, '') LIKE '%' + @Search + '%'
     OR ISNULL(b.Name, '') LIKE '%' + @Search + '%')
ORDER BY e.EmployeeNo;
""";

        Employees = await HrmsDatabase.QueryAsync(
            _dbContext,
            sql,
            command => HrmsDatabase.AddParameter(command, "@Search", Search),
            reader => new EmployeeProfileRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                Branch = HrmsDatabase.GetString(reader, "Branch"),
                Department = HrmsDatabase.GetString(reader, "Department"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                Gender = HrmsDatabase.GetString(reader, "Gender"),
                Nationality = HrmsDatabase.GetString(reader, "Nationality"),
                Country = HrmsDatabase.GetString(reader, "Country"),
                ContractType = HrmsDatabase.GetString(reader, "ContractType"),
                ContractEndDate = HrmsDatabase.GetDateOnly(reader, "ContractEndDate"),
                EmploymentStatus = HrmsDatabase.GetString(reader, "EmploymentStatus"),
                DirectManagerId = HrmsDatabase.GetInt(reader, "DirectManagerId") == 0 ? null : HrmsDatabase.GetInt(reader, "DirectManagerId"),
                DirectManager = HrmsDatabase.GetString(reader, "DirectManager")
            });
    }

    public class ProfileInput
    {
        public int Id { get; set; }

        public string? Position { get; set; }

        public string? Gender { get; set; }

        public string? Nationality { get; set; }

        public string? Country { get; set; }

        public string? ContractType { get; set; }

        public DateOnly? ContractEndDate { get; set; }

        public string? EmploymentStatus { get; set; }

        public int? DirectManagerId { get; set; }
    }

    public class EmployeeProfileRow : ProfileInput
    {
        public string EmployeeNo { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string Branch { get; set; } = string.Empty;

        public string Department { get; set; } = string.Empty;

        public string DirectManager { get; set; } = string.Empty;
    }

    public class EmployeeOption
    {
        public int Id { get; set; }

        public string Text { get; set; } = string.Empty;
    }
}
