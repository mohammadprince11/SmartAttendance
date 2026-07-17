using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Employees;

public class RehireModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public RehireModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty]
    public DateOnly? RehireDate { get; set; }

    [BindProperty]
    public string Reason { get; set; } = string.Empty;

    [BindProperty]
    public string HrNotes { get; set; } = string.Empty;

    [BindProperty]
    public bool ConfirmRehire { get; set; }

    public EmployeeRehireCard? Employee { get; set; }

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        await EmployeeLifecycleSchema.EnsureAsync(_dbContext);

        Employee = await LoadEmployeeAsync();

        if (Employee == null)
        {
            ErrorMessage = "لم يتم العثور على الموظف المطلوب.";
            return Page();
        }

        RehireDate = DateOnly.FromDateTime(DateTime.Today);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        await EmployeeLifecycleSchema.EnsureAsync(_dbContext);

        Employee = await LoadEmployeeAsync();

        if (Employee == null)
        {
            ErrorMessage = "لم يتم العثور على الموظف المطلوب.";
            return Page();
        }

        ValidateForm();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var userName = User.Identity?.Name ?? "System";
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var rehireDateValue = RehireDate!.Value;
        var rehireDateSql = rehireDateValue.ToDateTime(TimeOnly.MinValue);
        object previousHireDateSql = Employee.HireDate.HasValue
            ? Employee.HireDate.Value.ToDateTime(TimeOnly.MinValue)
            : DBNull.Value;

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
    EmploymentStatus = 'Active',
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
        'Employee Rehired',
        @OldValues,
        @NewValues,
        @CreatedBy,
        @IpAddress
    );
END;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", Id);
                HrmsDatabase.AddParameter(command, "@EmployeeNo", Employee.EmployeeNo);
                HrmsDatabase.AddParameter(command, "@EmployeeName", Employee.FullName);
                HrmsDatabase.AddParameter(command, "@PreviousHireDate", previousHireDateSql);
                HrmsDatabase.AddParameter(command, "@RehireDate", rehireDateSql);
                HrmsDatabase.AddParameter(command, "@PreviousEmploymentStatus", Employee.EmploymentStatus);
                HrmsDatabase.AddParameter(command, "@Reason", Reason.Trim());
                HrmsDatabase.AddParameter(command, "@HrNotes", string.IsNullOrWhiteSpace(HrNotes) ? DBNull.Value : HrNotes.Trim());
                HrmsDatabase.AddParameter(command, "@CreatedBy", userName);
                HrmsDatabase.AddParameter(command, "@IpAddress", ipAddress);
                HrmsDatabase.AddParameter(command, "@OldValues", $"IsActive: {Employee.IsActive}; HireDate: {DisplayDate(Employee.HireDate)}; EmploymentStatus: {Employee.EmploymentStatus}");
                HrmsDatabase.AddParameter(command, "@NewValues", $"IsActive: True; HireDate/RehireDate: {rehireDateValue:yyyy-MM-dd}; EmploymentStatus: Active");
            });

        TempData["SuccessMessage"] = "تمت إعادة تعيين الموظف بنفس بياناته السابقة وبتاريخ مباشرة جديد.";

        return RedirectToPage("./Profile", new { id = Id });
    }

    private void ValidateForm()
    {
        if (Employee?.IsActive == true)
        {
            ModelState.AddModelError(string.Empty, "هذا الموظف فعال حالياً ولا يحتاج إلى إعادة تعيين.");
        }

        if (!RehireDate.HasValue)
        {
            ModelState.AddModelError(nameof(RehireDate), "حدد تاريخ المباشرة الجديد.");
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            ModelState.AddModelError(nameof(Reason), "اكتب سبب إعادة التعيين.");
        }

        if (!ConfirmRehire)
        {
            ModelState.AddModelError(nameof(ConfirmRehire), "يجب تأكيد إعادة التعيين قبل الاعتماد.");
        }
    }

    private async Task<EmployeeRehireCard?> LoadEmployeeAsync()
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
    ISNULL(e.Position, '') AS Position,
    ISNULL(e.EmploymentStatus, '') AS EmploymentStatus,
    e.ServiceEndDate,
    ISNULL(e.ServiceEndType, '') AS ServiceEndType,
    ISNULL(e.ServiceEndReason, '') AS ServiceEndReason,
    ISNULL(d.Name, '') AS DepartmentName,
    ISNULL(b.Name, '') AS BranchName,
    ISNULL(c.Name, '') AS CompanyName
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON e.BranchId = b.Id
LEFT JOIN Companies c ON b.CompanyId = c.Id
WHERE e.Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", Id),
            reader => new EmployeeRehireCard
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                HireDate = HrmsDatabase.GetDateOnly(reader, "HireDate"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                EmploymentStatus = HrmsDatabase.GetString(reader, "EmploymentStatus"),
                ServiceEndDate = HrmsDatabase.GetDateOnly(reader, "ServiceEndDate"),
                ServiceEndType = HrmsDatabase.GetString(reader, "ServiceEndType"),
                ServiceEndReason = HrmsDatabase.GetString(reader, "ServiceEndReason"),
                DepartmentName = HrmsDatabase.GetString(reader, "DepartmentName"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName"),
                CompanyName = HrmsDatabase.GetString(reader, "CompanyName")
            });

        return rows.FirstOrDefault();
    }

    public string Display(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    public string DisplayDate(DateOnly? value)
    {
        return value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "-";
    }

    public class EmployeeRehireCard
    {
        public int Id { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public DateOnly? HireDate { get; set; }

        public bool IsActive { get; set; }

        public string Position { get; set; } = string.Empty;

        public string EmploymentStatus { get; set; } = string.Empty;

        public DateOnly? ServiceEndDate { get; set; }

        public string ServiceEndType { get; set; } = string.Empty;

        public string ServiceEndReason { get; set; } = string.Empty;

        public string DepartmentName { get; set; } = string.Empty;

        public string BranchName { get; set; } = string.Empty;

        public string CompanyName { get; set; } = string.Empty;
    }
}

