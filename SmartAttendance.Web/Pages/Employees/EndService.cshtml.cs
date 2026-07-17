using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Employees;

public class EndServiceModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public EndServiceModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty]
    public string EndServiceType { get; set; } = string.Empty;

    [BindProperty]
    public DateOnly? LastWorkingDate { get; set; }

    [BindProperty]
    public string Reason { get; set; } = string.Empty;

    [BindProperty]
    public string HrNotes { get; set; } = string.Empty;

    [BindProperty]
    public bool ClearanceAssets { get; set; }

    [BindProperty]
    public bool ClearanceDocuments { get; set; }

    [BindProperty]
    public bool ClearanceAccommodation { get; set; }

    [BindProperty]
    public bool ClearanceDevices { get; set; }

    [BindProperty]
    public bool ClearanceBadge { get; set; }

    [BindProperty]
    public bool ClearanceFinance { get; set; }

    [BindProperty]
    public bool ConfirmFinalAction { get; set; }

    public EmployeeEndServiceCard? Employee { get; set; }

    public string? ErrorMessage { get; set; }

    public string? SuccessMessage { get; set; }

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

        LastWorkingDate = DateOnly.FromDateTime(DateTime.Today);

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
        var status = MapEmploymentStatus(EndServiceType);
        var endTypeText = EndServiceTypeText(EndServiceType);
        var clearanceStatus = IsClearanceComplete() ? "Completed" : "Pending";

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            @"
INSERT INTO EmployeeEndServices
(
    EmployeeId,
    EmployeeNo,
    EmployeeName,
    EndServiceType,
    EndServiceTypeText,
    LastWorkingDate,
    Reason,
    HrNotes,
    ClearanceAssets,
    ClearanceDocuments,
    ClearanceAccommodation,
    ClearanceDevices,
    ClearanceBadge,
    ClearanceFinance,
    ClearanceStatus,
    CreatedBy,
    IpAddress,
    CreatedAt
)
VALUES
(
    @EmployeeId,
    @EmployeeNo,
    @EmployeeName,
    @EndServiceType,
    @EndServiceTypeText,
    @LastWorkingDate,
    @Reason,
    @HrNotes,
    @ClearanceAssets,
    @ClearanceDocuments,
    @ClearanceAccommodation,
    @ClearanceDevices,
    @ClearanceBadge,
    @ClearanceFinance,
    @ClearanceStatus,
    @CreatedBy,
    @IpAddress,
    GETDATE()
);

UPDATE Employees
SET
    IsActive = 0,
    EmploymentStatus = @EmploymentStatus,
    ServiceEndDate = @LastWorkingDate,
    ServiceEndType = @EndServiceType,
    ServiceEndReason = @Reason,
    ServiceEndNotes = @HrNotes,
    ClearanceStatus = @ClearanceStatus
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
        'Employee End Service',
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
                HrmsDatabase.AddParameter(command, "@EndServiceType", EndServiceType);
                HrmsDatabase.AddParameter(command, "@EndServiceTypeText", endTypeText);
                HrmsDatabase.AddParameter(command, "@LastWorkingDate", (object)LastWorkingDate!.Value.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@Reason", Reason.Trim());
                HrmsDatabase.AddParameter(command, "@HrNotes", string.IsNullOrWhiteSpace(HrNotes) ? DBNull.Value : HrNotes.Trim());
                HrmsDatabase.AddParameter(command, "@ClearanceAssets", ClearanceAssets);
                HrmsDatabase.AddParameter(command, "@ClearanceDocuments", ClearanceDocuments);
                HrmsDatabase.AddParameter(command, "@ClearanceAccommodation", ClearanceAccommodation);
                HrmsDatabase.AddParameter(command, "@ClearanceDevices", ClearanceDevices);
                HrmsDatabase.AddParameter(command, "@ClearanceBadge", ClearanceBadge);
                HrmsDatabase.AddParameter(command, "@ClearanceFinance", ClearanceFinance);
                HrmsDatabase.AddParameter(command, "@ClearanceStatus", clearanceStatus);
                HrmsDatabase.AddParameter(command, "@EmploymentStatus", status);
                HrmsDatabase.AddParameter(command, "@CreatedBy", userName);
                HrmsDatabase.AddParameter(command, "@IpAddress", ipAddress);
                HrmsDatabase.AddParameter(command, "@OldValues", "IsActive: True");
                HrmsDatabase.AddParameter(command, "@NewValues", $"IsActive: False; EmploymentStatus: {status}; ServiceEndDate: {LastWorkingDate:yyyy-MM-dd}; Type: {EndServiceType}");
            });

        TempData["SuccessMessage"] = "تم إنهاء خدمة الموظف مع الحفاظ على كامل التاريخ الوظيفي والحضور والسجلات.";

        return RedirectToPage("./Profile", new { id = Id });
    }

    private void ValidateForm()
    {
        if (string.IsNullOrWhiteSpace(EndServiceType))
        {
            ModelState.AddModelError(nameof(EndServiceType), "اختر نوع إنهاء الخدمة.");
        }

        if (!LastWorkingDate.HasValue)
        {
            ModelState.AddModelError(nameof(LastWorkingDate), "حدد آخر يوم عمل.");
        }

        if (LastWorkingDate.HasValue && Employee?.HireDate != null && LastWorkingDate.Value < Employee.HireDate.Value)
        {
            ModelState.AddModelError(nameof(LastWorkingDate), "آخر يوم عمل لا يمكن أن يكون قبل تاريخ التعيين.");
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            ModelState.AddModelError(nameof(Reason), "اكتب سبب إنهاء الخدمة.");
        }

        if (!ConfirmFinalAction)
        {
            ModelState.AddModelError(nameof(ConfirmFinalAction), "يجب تأكيد الإجراء النهائي قبل الحفظ.");
        }
    }

    private async Task<EmployeeEndServiceCard?> LoadEmployeeAsync()
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
    ISNULL(d.Name, '') AS DepartmentName,
    ISNULL(b.Name, '') AS BranchName,
    ISNULL(c.Name, '') AS CompanyName
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON e.BranchId = b.Id
LEFT JOIN Companies c ON b.CompanyId = c.Id
WHERE e.Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", Id),
            reader => new EmployeeEndServiceCard
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                HireDate = HrmsDatabase.GetDateOnly(reader, "HireDate"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                EmploymentStatus = HrmsDatabase.GetString(reader, "EmploymentStatus"),
                DepartmentName = HrmsDatabase.GetString(reader, "DepartmentName"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName"),
                CompanyName = HrmsDatabase.GetString(reader, "CompanyName")
            });

        return rows.FirstOrDefault();
    }

    private bool IsClearanceComplete()
    {
        return ClearanceAssets
            && ClearanceDocuments
            && ClearanceAccommodation
            && ClearanceDevices
            && ClearanceBadge
            && ClearanceFinance;
    }

    private string MapEmploymentStatus(string value)
    {
        return value switch
        {
            "Resignation" => "Resigned",
            "ContractEnd" => "Contract Ended",
            "Termination" => "Terminated",
            "NonRenewal" => "Non-Renewed",
            "JobAbandonment" => "Job Abandonment",
            "Death" => "Ended",
            _ => "Ended"
        };
    }

    public string EndServiceTypeText(string? value)
    {
        return value switch
        {
            "Resignation" => "استقالة",
            "ContractEnd" => "انتهاء عقد",
            "Termination" => "فصل",
            "NonRenewal" => "عدم تجديد عقد",
            "JobAbandonment" => "ترك عمل",
            "Death" => "وفاة",
            "Other" => "أخرى",
            _ => "-"
        };
    }

    public string Display(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    public string DisplayDate(DateOnly? value)
    {
        return value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "-";
    }

    public class EmployeeEndServiceCard
    {
        public int Id { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public DateOnly? HireDate { get; set; }

        public bool IsActive { get; set; }

        public string Position { get; set; } = string.Empty;

        public string EmploymentStatus { get; set; } = string.Empty;

        public string DepartmentName { get; set; } = string.Empty;

        public string BranchName { get; set; } = string.Empty;

        public string CompanyName { get; set; } = string.Empty;
    }
}

