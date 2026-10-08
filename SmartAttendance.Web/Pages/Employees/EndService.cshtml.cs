using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Employees;

public class EndServiceModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyScopeProvider _companyScope;

    public EndServiceModel(ApplicationDbContext dbContext, ICompanyScopeProvider companyScope)
    {
        _dbContext = dbContext;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty]
    public string EndServiceType { get; set; } = string.Empty;

    [BindProperty]
    public DateOnly? LastWorkingDate { get; set; }

    [BindProperty]
    public bool ImmediateAccountClosure { get; set; }

    [BindProperty]
    public string Reason { get; set; } = string.Empty;

    [BindProperty]
    public string? HrNotes { get; set; }

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

        if (!await EndServiceAccessStore.IsReadyAsync(_dbContext))
        {
            ErrorMessage = "يلزم تطبيق هجرة توقيت إيقاف الحساب قبل اعتماد إنهاء الخدمة.";
            return Page();
        }
        var timing = EndServiceAccessPolicy.Plan(LastWorkingDate!.Value, ImmediateAccountClosure, DateTimeOffset.UtcNow);

        var userName = User.Identity?.Name ?? "System";
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var status = MapEmploymentStatus(EndServiceType);
        var endTypeText = EndServiceTypeText(EndServiceType);
        var clearanceStatus = IsClearanceComplete() ? "Completed" : "Pending";

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            @"
-- ذرّية + idempotency: السجلّ والتعطيل والتدقيق دفعةٌ واحدة كلٌّ-أو-لا-شيء
-- (XACT_ABORT يُرجِع كل شيء عند أي خطأ)، والحارس IsActive=1 يمنع إعادة الإرسال/
-- النقرة المزدوجة من إنشاء سجلَّي إنهاءٍ مكرَّرين. موظفٌ أُعيد تعيينه يعود IsActive=1
-- فيُنهى ثانيةً بمشروعية.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF EXISTS (SELECT 1 FROM Employees WITH (UPDLOCK, HOLDLOCK)
           WHERE Id = @EmployeeId AND CompanyId = @CompanyId AND IsDeleted = 0 AND ISNULL(IsActive, 0) = 1)
BEGIN
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

    INSERT EndServiceAccessSchedules
        (EndServiceId, EmployeeId, CompanyId, NotificationEligibleAtUtc, AccessEndsAtUtc, Immediate)
    VALUES (CONVERT(int, SCOPE_IDENTITY()), @EmployeeId, @CompanyId, @NotifyAt, @AccessEnds, @Immediate);

    UPDATE Employees
    SET
        IsActive = 0,
        EmploymentStatus = @EmploymentStatus,
        ServiceEndDate = @LastWorkingDate,
        ServiceEndType = @EndServiceType,
        ServiceEndReason = @Reason,
        ServiceEndNotes = @HrNotes,
        ClearanceStatus = @ClearanceStatus
    WHERE Id = @EmployeeId AND CompanyId = @CompanyId;

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
    END;
END;

COMMIT TRANSACTION;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", Id);
                HrmsDatabase.AddParameter(command, "@CompanyId", Employee.CompanyId);
                HrmsDatabase.AddParameter(command, "@NotifyAt", timing.NotificationEligibleAtUtc.UtcDateTime);
                HrmsDatabase.AddParameter(command, "@AccessEnds", timing.AccessEndsAtUtc.UtcDateTime);
                HrmsDatabase.AddParameter(command, "@Immediate", timing.Immediate);
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
                HrmsDatabase.AddParameter(command, "@NewValues", $"IsActive: False; EmploymentStatus: {status}; ServiceEndDate: {LastWorkingDate:yyyy-MM-dd}; Type: {EndServiceType}; AccessEndsAtUtc: {timing.AccessEndsAtUtc:O}; FarewellOnly: True; Immediate: {timing.Immediate}");
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

        if (LastWorkingDate == DateOnly.MaxValue)
            ModelState.AddModelError(nameof(LastWorkingDate), "اختر تاريخاً يسمح بتحديد نهاية اليوم.");

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
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (scope.IsDeniedAll) return null;
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            $@"
SELECT TOP 1
    e.Id,
    e.CompanyId,
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
LEFT JOIN Companies c ON e.CompanyId = c.Id
WHERE e.Id = @Id AND e.IsDeleted = 0 AND {scope.ToSqlPredicate("e.CompanyId")};",
            command => HrmsDatabase.AddParameter(command, "@Id", Id),
            reader => new EmployeeEndServiceCard
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                CompanyId = HrmsDatabase.GetInt(reader, "CompanyId"),
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
        public int CompanyId { get; set; }
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

