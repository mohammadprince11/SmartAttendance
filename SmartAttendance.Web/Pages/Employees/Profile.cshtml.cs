using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Application.PeopleAi;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace SmartAttendance.Web.Pages.Employees;

public partial class ProfileModel : PageModel
{
    private static readonly HashSet<string> AllowedEmployeePhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp"
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;
    private readonly IPermissionAuthorizationService _permissionAuthorizationService;

    private readonly Infrastructure.Security.IProtectedFileService _protectedFiles;
    private readonly Infrastructure.Security.IFileThreatScanner _threatScanner;
    private readonly Infrastructure.Security.MalwareScanningOptions _malwareOptions;

    // لتمرير نطاق الشركات للمسار القانونيّ لحذف العقود (ContractRegisterStore).
    private readonly Infrastructure.Security.ICompanyScopeProvider _companyScope;

    public ProfileModel(
        ApplicationDbContext dbContext,
        IWebHostEnvironment environment,
        IPermissionAuthorizationService permissionAuthorizationService,
        Infrastructure.Security.IProtectedFileService protectedFiles,
        Infrastructure.Security.IFileThreatScanner threatScanner,
        IOptions<Infrastructure.Security.MalwareScanningOptions> malwareOptions,
        Infrastructure.Security.ICompanyScopeProvider companyScope)
    {
        _dbContext = dbContext;
        _environment = environment;
        _permissionAuthorizationService = permissionAuthorizationService;
        _protectedFiles = protectedFiles;
        _threatScanner = threatScanner;
        _malwareOptions = malwareOptions.Value;
        _companyScope = companyScope;
    }

    /// <summary>رابط فتح أي مرفق بملف الموظف عبر نقطة التنزيل المصادَقة.</summary>
    public string FileUrl(string? storedPath) => _protectedFiles.BuildUrl(Id, storedPath);

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EmployeeNo { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? ToDate { get; set; }

    public int ActivityPageSize => 25;

    [BindProperty(SupportsGet = true)]
    public int TimelinePage { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int AuditPage { get; set; } = 1;

    public int TimelineTotalCount { get; set; }
    public int AuditTotalCount { get; set; }
    public int TimelineTotalPages => Math.Max(1, (TimelineTotalCount + ActivityPageSize - 1) / ActivityPageSize);
    public int AuditTotalPages => Math.Max(1, (AuditTotalCount + ActivityPageSize - 1) / ActivityPageSize);

    public EmployeeProfileCard? Employee { get; set; }

    public EmployeeProfileIntelligenceResult? ProfileIntelligence { get; set; }

    public List<AttendanceRow> AttendanceRows { get; set; } = new();

    public List<RequestRow> RequestRows { get; set; } = new();

    public List<DayRequestOption> DayRequestOptions { get; set; } = new();
    public List<DayShiftOption> DayShiftOptions { get; set; } = new();

    public List<DocumentRow> DocumentRows { get; set; } = new();

    public List<ShiftRow> ShiftRows { get; set; } = new();

    public List<AuditRow> AuditRows { get; set; } = new();

    public List<EmployeeProfileDynamicSection> ProfileDynamicSections { get; set; } = new();

    public int AttendanceCount { get; set; }

    public int PresentCount { get; set; }

    public int LateCount { get; set; }

    public int AbsentCount { get; set; }

    /// <summary>
    /// عدد اليوميات المحلَّلة بالفترة. يميّز «صفر تأخير» عن «لم يُحلَّل الحضور بعد»
    /// — وبدونه تُقرأ الأصفار كأنها انضباط تامّ.
    /// </summary>
    public int AnalyzedDays { get; set; }

    public int MissingCheckoutCount { get; set; }

    public int TotalWorkingMinutes { get; set; }

    public int PendingRequests { get; set; }

    public int TimePickerStepMinutes { get; set; } = 30;

    public string? AttendanceCutoffPolicyName { get; set; }
    public DateOnly AttendancePolicyFrom { get; set; }
    public DateOnly AttendancePolicyTo { get; set; }
    public bool HasAttendanceCutoffPolicy => !string.IsNullOrWhiteSpace(AttendanceCutoffPolicyName);

    public int ApprovedRequests { get; set; }

    public int RejectedRequests { get; set; }

    public string? ErrorMessage { get; set; }

    public bool CanEditEmployee { get; set; }

    public bool CanDeleteDocument { get; set; }

    public bool CanChangeAssignment { get; set; }

    public bool CanEndService { get; set; }

    public bool CanRehire { get; set; }

    public bool CanViewLifecycle { get; set; }

    public bool CanViewHistory { get; set; }

    public bool CanOpenEmployeeDocuments { get; set; }

    public bool CanViewDirectory { get; set; }

    [BindProperty]
    public IFormFile? EmployeePhoto { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await EmployeeLifecycleSchema.EnsureAsync(_dbContext);
        await EnsureProfileFilesTableAsync();
        TimePickerStepMinutes = await AttendanceRequestPolicy.GetTimePickerStepMinutesAsync(_dbContext);

        Employee = await LoadEmployeeAsync();

        if (Employee == null)
        {
            ErrorMessage = "لم يتم العثور على الموظف المطلوب.";
            return Page();
        }

        ProfileDynamicSections = await EmployeeProfileDynamicFields.LoadSectionsAsync(_dbContext, Employee.Id);

        // الفترة الافتراضية مرتبطة بسياسة إقفال الحضور الخاصة بشركة الموظف.
        var today = DateOnly.FromDateTime(DateTime.Today);
        var (attendancePeriod, cutoffPolicyName) = await AttendancePeriodPolicy.ResolveFromPolicyAsync(
            _dbContext,
            today.Year,
            today.Month,
            SmartAttendance.Domain.Enums.PayrollCutoffType.Attendance,
            Employee.CompanyId);

        AttendanceCutoffPolicyName = cutoffPolicyName;
        AttendancePolicyFrom = attendancePeriod.From;
        AttendancePolicyTo = attendancePeriod.To;

        if (!FromDate.HasValue || !ToDate.HasValue)
        {
            FromDate ??= attendancePeriod.From;
            ToDate ??= attendancePeriod.To;
        }

        await LoadActionPermissionsAsync(Employee.Id);

        if (CanRehire)
        {
            await LoadProfileReassignLookupsAsync();
        }

        await LoadAttendanceAsync(Employee.Id);
        await LoadDayRequestOptionsAsync(Employee.Id);
        await LoadRequestsAsync(Employee.Id);
        await LoadDocumentsAsync(Employee.Id);
        await LoadProfileFilesAsync(Employee.Id);
        await LoadShiftsAsync(Employee.Id);

        if (CanViewHistory)
        {
            await LoadAuditAsync(Employee.Id);
        }

        Id = Employee.Id;
        await LoadPanelsAsync();
        BuildProfileIntelligence();
        await LoadTimelineAsync();
        BuildEmployeeRequirements();

        return Page();
    }

    public async Task<IActionResult> OnGetDayPunchPairsAsync(int id, string? date)
    {
        if (!DateOnly.TryParse(date, out var workDate))
            return new JsonResult(new { ok = false, message = "التاريخ غير صالح." });

        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (!await SmartAttendance.Web.Infrastructure.Security.EmployeeCompanyGuard.CanAccessEmployeeAsync(
                _dbContext, id, scope, HttpContext.RequestAborted))
            return NotFound();

        var pairs = await AttendancePunchEditorStore.LoadAsync(_dbContext, scope, id, workDate);
        return new JsonResult(new
        {
            ok = true,
            pairs = pairs.Select(pair => new
            {
                id = pair.Id,
                checkIn = pair.CheckIn,
                checkOut = pair.CheckOut
            })
        });
    }

    public async Task<IActionResult> OnPostDayPunchEditAsync(
        int id, string? dayDate,
        List<AttendancePunchEditorStore.PairInput> punchPairs,
        string? editReason,
        DateOnly? returnFromDate, DateOnly? returnToDate)
    {
        if (!DateOnly.TryParse(dayDate, out var workDate))
        {
            TempData["ErrorMessage"] = "تعذر تحديد تاريخ اليوم المطلوب.";
            return RedirectToProfile(id, returnFromDate, returnToDate);
        }

        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var result = await AttendancePunchEditorStore.SaveAsync(
            _dbContext,
            scope,
            id,
            workDate,
            punchPairs ?? new List<AttendancePunchEditorStore.PairInput>(),
            editReason,
            User.Identity?.Name ?? "HR",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData[result.Ok ? "SuccessMessage" : "ErrorMessage"] = result.Message;
        return RedirectToProfile(id, returnFromDate, returnToDate);
    }


    public async Task<IActionResult> OnPostDayPunchRequestAsync(
        int id, string? dayDate, string? punchTime, string? dayReason,
        DateOnly? returnFromDate, DateOnly? returnToDate)
    {
        if (!DateOnly.TryParse(dayDate, out var date) || !TimeOnly.TryParse(punchTime, out var time))
        {
            TempData["ErrorMessage"] = "حدد تاريخ اليوم ووقت البصمة المطلوب إضافتها.";
            return RedirectToProfile(id, returnFromDate, returnToDate);
        }

        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (!await SmartAttendance.Web.Infrastructure.Security.EmployeeCompanyGuard.CanAccessEmployeeAsync(
                _dbContext, id, scope, HttpContext.RequestAborted))
        {
            return NotFound();
        }

        var eligibility = await EmployeeRequestEligibility.CheckAsync(
            _dbContext, id, HttpContext.RequestAborted);
        if (!eligibility.IsEligible)
        {
            TempData["ErrorMessage"] = eligibility.Message;
            return RedirectToProfile(id, returnFromDate, returnToDate);
        }
        var punchAt = date.ToDateTime(time);
        var existingTimes = await PunchTypingEngine.DayPunchTimesAsync(_dbContext, id, date);
        var punchType = PunchTypingEngine.DeriveTypeFor(existingTimes, punchAt);
        var (ok, message) = await MissingPunchRequestStore.SaveAsync(
            _dbContext, scope, new MissingPunchRequestStore.Request
            {
                EmployeeId = id,
                PunchAt = punchAt,
                PunchType = punchType,
                Reason = string.IsNullOrWhiteSpace(dayReason) ? null : dayReason.Trim(),
                Source = "ملف الموظف"
            }, User.Identity?.Name ?? "HR", id);

        TempData[ok ? "SuccessMessage" : "ErrorMessage"] = ok
            ? $"{message} الطلب الآن قيد المراجعة ضمن الحضور والانصراف > طلبات البصمة المفقودة."
            : message;
        return RedirectToProfile(id, returnFromDate, returnToDate);
    }

    public async Task<IActionResult> OnPostDayRequestAsync(
        int id, string? dayDate, string? requestOption,
        string? requestStartTime, string? requestEndTime, string? requestReason,
        IFormFile? requestAttachment, int? requestShiftTypeId,
        DateOnly? returnFromDate, DateOnly? returnToDate)
    {
        if (!DateOnly.TryParse(dayDate, out var date))
        {
            TempData["ErrorMessage"] = "تعذر تحديد تاريخ اليوم المطلوب.";
            return RedirectToProfile(id, returnFromDate, returnToDate);
        }

        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (!await SmartAttendance.Web.Infrastructure.Security.EmployeeCompanyGuard.CanAccessEmployeeAsync(
                _dbContext, id, scope, HttpContext.RequestAborted))
            return NotFound();

        var eligibility = await EmployeeRequestEligibility.CheckAsync(
            _dbContext, id, HttpContext.RequestAborted);
        if (!eligibility.IsEligible)
        {
            TempData["ErrorMessage"] = eligibility.Message;
            return RedirectToProfile(id, returnFromDate, returnToDate);
        }

        await LoadDayRequestOptionsAsync(id);
        var option = DayRequestOptions.FirstOrDefault(item => item.Value == requestOption);        if (option == null)
        {
            TempData["ErrorMessage"] = "نوع الطلب غير متاح لهذا الموظف حالياً.";
            return RedirectToProfile(id, returnFromDate, returnToDate);
        }

        TimeSpan? startTime = null;
        TimeSpan? endTime = null;
        var toDate = date;
        if (option.NeedsTime)
        {
            if (!TimeSpan.TryParse(requestStartTime, out var st) ||
                !TimeSpan.TryParse(requestEndTime, out var et))
            {
                TempData["ErrorMessage"] = "حدد وقت البداية والنهاية لهذا النوع من الطلبات.";
                return RedirectToProfile(id, returnFromDate, returnToDate);
            }
            startTime = st;
            endTime = et;
            if (et <= st)
            {
                if (!await AttendanceRequestPolicy.GetCrossMidnightAsync(_dbContext))
                {
                    TempData["ErrorMessage"] = "وقت النهاية يجب أن يكون بعد البداية؛ عبور منتصف الليل معطّل من إعدادات الحضور.";
                    return RedirectToProfile(id, returnFromDate, returnToDate);
                }
                toDate = date.AddDays(1);
            }
        }

        int? shiftTypeId = null;
        if (option.NeedsShift)
        {
            var validShift = requestShiftTypeId is > 0 &&
                DayShiftOptions.Any(shift => shift.Id == requestShiftTypeId.Value);
            if (!validShift)
            {
                TempData["ErrorMessage"] = "اختر المناوبة الجديدة لهذا الطلب.";
                return RedirectToProfile(id, returnFromDate, returnToDate);
            }
            shiftTypeId = requestShiftTypeId;
        }

        if (option.AttachmentRequired && requestAttachment is not { Length: > 0 })
        {
            var label = string.IsNullOrWhiteSpace(option.AttachmentLabel)
                ? "المرفق مطلوب لهذا الطلب."
                : $"المرفق مطلوب: {option.AttachmentLabel}.";
            TempData["ErrorMessage"] = label;            return RedirectToProfile(id, returnFromDate, returnToDate);
        }

        string? attachmentPath = null;
        if (requestAttachment is { Length: > 0 })
        {
            attachmentPath = await _protectedFiles.SaveAsync(
                requestAttachment, id, "request", HttpContext.RequestAborted);
            if (attachmentPath == null)
            {
                TempData["ErrorMessage"] = "المرفق غير صالح؛ استخدم PDF أو صورة ضمن الحجم المسموح.";
                return RedirectToProfile(id, returnFromDate, returnToDate);
            }
        }

        var actor = User.Identity?.Name ?? "HR";
        var reason = string.IsNullOrWhiteSpace(requestReason)
            ? $"{option.Label} ليوم {date:yyyy-MM-dd} من ملف الموظف"
            : requestReason.Trim();

        int requestId;
        try
        {
            requestId = await HrmsDatabase.ScalarAsync<int>(
                _dbContext,
            """
INSERT INTO SelfServiceRequests
(EmployeeId, RequestTypeId, RequestType, RequestDate, FromDate, ToDate, StartTime, EndTime, Reason,
 Status, CurrentStep, CreatedBy, RequestSource, DaysCount, AttachmentPath, ShiftTypeId)
VALUES
(@EmployeeId, @RequestTypeId, @RequestType, @RequestDate, @FromDate, @ToDate, @StartTime, @EndTime, @Reason,
 'Pending', 'Direct Manager', @CreatedBy, @RequestSource, 1, @AttachmentPath, @ShiftTypeId);SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", id);
                HrmsDatabase.AddParameter(command, "@RequestTypeId", (object?)option.RequestTypeId ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@RequestType", option.RequestType);
                HrmsDatabase.AddParameter(command, "@RequestDate", DateOnly.FromDateTime(DateTime.Today));
                HrmsDatabase.AddParameter(command, "@FromDate", date);
                HrmsDatabase.AddParameter(command, "@ToDate", toDate);
                HrmsDatabase.AddParameter(command, "@StartTime", (object?)startTime ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@EndTime", (object?)endTime ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Reason", reason);
                HrmsDatabase.AddParameter(command, "@CreatedBy", actor);
                HrmsDatabase.AddParameter(command, "@RequestSource", RequestSourceCatalog.Admin);
                HrmsDatabase.AddParameter(command, "@AttachmentPath", (object?)attachmentPath ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@ShiftTypeId", (object?)shiftTypeId ?? DBNull.Value);
                });
        }
        catch (SqlException ex) when (ex.Number is 547 or 515 or 2601 or 2627)
        {
            TempData["ErrorMessage"] = "تعذر إنشاء الطلب لأن البيانات لا تتوافق مع إعدادات النظام الحالية. راجع نوع الطلب وسياسة الشركة ثم أعد المحاولة.";
            return RedirectToProfile(id, returnFromDate, returnToDate);
        }

        if (requestId <= 0)
        {
            TempData["ErrorMessage"] = "تعذر إنشاء الطلب.";
            return RedirectToProfile(id, returnFromDate, returnToDate);
        }

        var start = await ApprovalWorkflowEngine.StartAsync(
            _dbContext, requestId, option.RequestType, id);
        TempData[start.Ok ? "SuccessMessage" : "ErrorMessage"] = start.Ok
            ? $"تم إرسال {option.Label} للموافقة."
            : start.Message;
        return RedirectToProfile(id, returnFromDate, returnToDate);
    }

    private IActionResult RedirectToProfile(int id, DateOnly? from, DateOnly? to)
    {
        return RedirectToPage("./Profile", new
        {
            id,
            FromDate = from?.ToString("yyyy-MM-dd"),
            ToDate = to?.ToString("yyyy-MM-dd")
        });
    }

    public async Task<IActionResult> OnPostUploadProfilePhotoAsync(int id)
    {
        await EmployeeLifecycleSchema.EnsureAsync(_dbContext);

        if (id <= 0)
        {
            TempData["ErrorMessage"] = "لم يتم تحديد الموظف.";
            return RedirectToPage("./Index");
        }

        var result = await SaveEmployeePhotoAsync(id, EmployeePhoto);

        if (string.IsNullOrWhiteSpace(result))
        {
            TempData["ErrorMessage"] = "اختر صورة صحيحة بصيغة PNG أو JPG أو WEBP وبحجم لا يتجاوز 5MB.";
        }
        else
        {
            TempData["SuccessMessage"] = "تم تحديث صورة الموظف بنجاح.";
        }

        return RedirectToPage(new { id });
    }

    private async Task<string> SaveEmployeePhotoAsync(int employeeId, IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return string.Empty;
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedEmployeePhotoExtensions.Contains(extension))
        {
            return string.Empty;
        }

        if (file.Length > 5 * 1024 * 1024)
        {
            return string.Empty;
        }

        if (!await UploadSignatureValidator.IsValidImageAsync(file))
        {
            return string.Empty;
        }

        var uploadsRoot = Path.Combine(_environment.WebRootPath, "uploads", "employee-photos");
        Directory.CreateDirectory(uploadsRoot);

        var fileName = $"employee_{employeeId}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension.ToLowerInvariant()}";
        var fullPath = Path.Combine(uploadsRoot, fileName);

        await using (var stream = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(stream);
        }

        var relativePath = $"/uploads/employee-photos/{fileName}";

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            "UPDATE Employees SET PhotoPath = @PhotoPath WHERE Id = @EmployeeId;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@PhotoPath", relativePath);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
            });

        return relativePath;
    }

    private async Task LoadActionPermissionsAsync(int employeeId)
    {
        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var role = PeopleAccessContext.GetRole(HttpContext);

        CanViewDirectory = await _permissionAuthorizationService.HasPermissionAsync(
            systemUserId,
            PeoplePermissionCodes.ViewDirectory,
            PeopleCompatibilityAccess.IsAllowed(
                role,
                PeoplePermissionCodes.ViewDirectory),
            HttpContext.RequestAborted);

        CanEditEmployee = await CanAccessAsync(
            systemUserId,
            role,
            PeoplePermissionCodes.Edit,
            employeeId);
        CanDeleteDocument = await CanAccessAsync(
            systemUserId,
            role,
            PeoplePermissionCodes.DeleteDocument,
            employeeId);
        CanChangeAssignment = await CanAccessAsync(
            systemUserId,
            role,
            PeoplePermissionCodes.ChangeAssignment,
            employeeId);
        CanEndService = await CanAccessAsync(
            systemUserId,
            role,
            PeoplePermissionCodes.EndService,
            employeeId);
        CanRehire = await CanAccessAsync(
            systemUserId,
            role,
            PeoplePermissionCodes.Rehire,
            employeeId);
        CanViewLifecycle = await CanAccessAsync(
            systemUserId,
            role,
            PeoplePermissionCodes.ViewLifecycle,
            employeeId);
        CanViewHistory = await CanAccessAsync(
            systemUserId,
            role,
            PeoplePermissionCodes.ViewHistory,
            employeeId);

        // The legacy document centre is still an administrator-only aggregate
        // screen. Scoped document access remains available through profile cards.
        CanOpenEmployeeDocuments = role.Equals(
            "Admin",
            StringComparison.OrdinalIgnoreCase);
    }

    private Task<bool> CanAccessAsync(
        int systemUserId,
        string role,
        string permissionCode,
        int employeeId)
    {
        return _permissionAuthorizationService.CanAccessEmployeeAsync(
            systemUserId,
            permissionCode,
            employeeId,
            PeopleCompatibilityAccess.IsAllowed(role, permissionCode),
            HttpContext.RequestAborted);
    }

    private Task<bool> HasEmployeeActionPermissionAsync(
        string permissionCode,
        int employeeId)
    {
        var systemUserId =
            PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var role = PeopleAccessContext.GetRole(HttpContext);

        return CanAccessAsync(
            systemUserId,
            role,
            permissionCode,
            employeeId);
    }

    private async Task<EmployeeProfileCard?> LoadEmployeeAsync()
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT TOP 1
    e.Id,
    COALESCE(NULLIF(e.CompanyId, 0), b.CompanyId, 0) AS CompanyId,
    e.BranchId AS BranchId,
    e.DepartmentId AS DepartmentId,
    e.PositionId AS PositionId,
    e.DirectManagerId AS DirectManagerId,
    e.EmployeeNo,
    e.FullName,
    ISNULL(e.NationalId, '') AS NationalId,
    ISNULL(e.Phone, '') AS Phone,
    ISNULL(e.Email, '') AS Email,
    e.HireDate,
    e.BirthDate,
    ISNULL(e.MaritalStatus, '') AS MaritalStatus,
    e.IsActive,
    ISNULL(e.Position, '') AS Position,
    ISNULL(e.PhotoPath, '') AS PhotoPath,
    ISNULL(e.Gender, '') AS Gender,
    ISNULL(e.Nationality, '') AS Nationality,
    ISNULL(e.Country, '') AS Country,
    ISNULL(e.ContractType, '') AS ContractType,
    e.ContractEndDate,
    ISNULL(e.EmploymentStatus, '') AS EmploymentStatus,
    e.IsCitizen,
    ISNULL(e.PassportNo, '') AS PassportNo,
    ISNULL(e.SponsorName, '') AS SponsorName,
    ISNULL(e.Religion, '') AS Religion,
    ISNULL(e.PersonalEmail, '') AS PersonalEmail,
    ISNULL(e.PhoneExtension, '') AS PhoneExtension,
    e.JoiningDate,
    ISNULL(e.WorkType, '') AS WorkType,
    ISNULL(e.JobGrade, '') AS JobGrade,
    ISNULL(d.Name, '') AS DepartmentName,
    ISNULL(b.Name, '') AS BranchName,
    ISNULL(c.Name, '') AS CompanyName,
    ISNULL(m.FullName, '') AS DirectManager
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON e.BranchId = b.Id
LEFT JOIN Companies c ON b.CompanyId = c.Id
LEFT JOIN Employees m ON e.DirectManagerId = m.Id
WHERE
    (@Id > 0 AND e.Id = @Id)
    OR
    (@Id = 0 AND @EmployeeNo <> '' AND e.EmployeeNo = @EmployeeNo);",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", Id);
                HrmsDatabase.AddParameter(command, "@EmployeeNo", EmployeeNo ?? "");
            },
            reader => new EmployeeProfileCard
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                CompanyId = HrmsDatabase.GetInt(reader, "CompanyId"),
                BranchId = HrmsDatabase.GetInt(reader, "BranchId"),
                DepartmentId = HrmsDatabase.GetInt(reader, "DepartmentId"),
                PositionId = reader["PositionId"] is DBNull ? null : Convert.ToInt32(reader["PositionId"]),
                DirectManagerId = reader["DirectManagerId"] is DBNull ? null : Convert.ToInt32(reader["DirectManagerId"]),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                NationalId = HrmsDatabase.GetString(reader, "NationalId"),
                Phone = HrmsDatabase.GetString(reader, "Phone"),
                Email = HrmsDatabase.GetString(reader, "Email"),
                HireDate = HrmsDatabase.GetDateOnly(reader, "HireDate"),
                BirthDate = HrmsDatabase.GetDateOnly(reader, "BirthDate"),
                MaritalStatus = HrmsDatabase.GetString(reader, "MaritalStatus"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                PhotoPath = HrmsDatabase.GetString(reader, "PhotoPath"),
                Gender = HrmsDatabase.GetString(reader, "Gender"),
                Nationality = HrmsDatabase.GetString(reader, "Nationality"),
                Country = HrmsDatabase.GetString(reader, "Country"),
                ContractType = HrmsDatabase.GetString(reader, "ContractType"),
                ContractEndDate = HrmsDatabase.GetDateOnly(reader, "ContractEndDate"),
                EmploymentStatus = HrmsDatabase.GetString(reader, "EmploymentStatus"),
                IsCitizen = HrmsDatabase.GetBool(reader, "IsCitizen"),
                PassportNo = HrmsDatabase.GetString(reader, "PassportNo"),
                SponsorName = HrmsDatabase.GetString(reader, "SponsorName"),
                Religion = HrmsDatabase.GetString(reader, "Religion"),
                PersonalEmail = HrmsDatabase.GetString(reader, "PersonalEmail"),
                PhoneExtension = HrmsDatabase.GetString(reader, "PhoneExtension"),
                JoiningDate = HrmsDatabase.GetDateOnly(reader, "JoiningDate"),
                WorkType = HrmsDatabase.GetString(reader, "WorkType"),
                JobGrade = HrmsDatabase.GetString(reader, "JobGrade"),
                DepartmentName = HrmsDatabase.GetString(reader, "DepartmentName"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName"),
                CompanyName = HrmsDatabase.GetString(reader, "CompanyName"),
                DirectManager = HrmsDatabase.GetString(reader, "DirectManager")
            });

        var employee = rows.FirstOrDefault();

        if (employee is not null)
        {
            await LocalizeEmployeeProfileAsync(employee);
        }

        return employee;
    }


    private async Task LocalizeEmployeeProfileAsync(
        EmployeeProfileCard employee)
    {
        if (employee.CompanyId <= 0)
        {
            return;
        }

        var languages = await _dbContext.CompanyLanguages
            .AsNoTracking()
            .Where(item =>
                item.CompanyId == employee.CompanyId &&
                item.IsActive &&
                !item.IsDeleted)
            .Select(item => new
            {
                item.CultureCode,
                item.IsDefault
            })
            .ToListAsync(HttpContext.RequestAborted);

        if (languages.Count == 0)
        {
            return;
        }

        static string NormalizeCulture(string? culture)
        {
            if (string.IsNullOrWhiteSpace(culture))
            {
                return string.Empty;
            }

            try
            {
                return CultureInfo.GetCultureInfo(culture.Trim()).Name;
            }
            catch (CultureNotFoundException)
            {
                return culture.Trim();
            }
        }

        static string LanguageFamily(string? culture)
        {
            var normalized = NormalizeCulture(culture);
            var separator = normalized.IndexOf('-');

            return separator > 0
                ? normalized[..separator]
                : normalized;
        }

        var requested = NormalizeCulture(
            CultureInfo.CurrentUICulture.Name);
        var requestedFamily = LanguageFamily(requested);

        var configured = languages
            .Select(item => new
            {
                CultureCode = NormalizeCulture(item.CultureCode),
                item.IsDefault
            })
            .ToList();

        var fallback = configured
            .FirstOrDefault(item => item.IsDefault)
            ?.CultureCode;

        var preferred = configured
            .FirstOrDefault(item =>
                item.CultureCode.Equals(
                    requested,
                    StringComparison.OrdinalIgnoreCase))
            ?.CultureCode;

        preferred ??= configured
            .FirstOrDefault(item =>
                LanguageFamily(item.CultureCode).Equals(
                    requestedFamily,
                    StringComparison.OrdinalIgnoreCase))
            ?.CultureCode;

        preferred ??= fallback;

        var cultures = new[] { preferred, fallback }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (cultures.Length == 0)
        {
            return;
        }

        var employeeIds = new[]
            {
                employee.Id,
                employee.DirectManagerId ?? 0
            }
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        var entityIds = new[]
            {
                employee.CompanyId,
                employee.BranchId,
                employee.DepartmentId,
                employee.PositionId ?? 0
            }
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        var values = await _dbContext.LocalizedEntityValues
            .AsNoTracking()
            .Where(item =>
                item.CompanyId == employee.CompanyId &&
                cultures.Contains(item.CultureCode) &&
                !item.IsDeleted &&
                (
                    (item.EntityType == "Employee" &&
                     employeeIds.Contains(item.EntityId)) ||
                    ((item.EntityType == "Company" ||
                      item.EntityType == "Branch" ||
                      item.EntityType == "Department" ||
                      item.EntityType == "Position") &&
                     entityIds.Contains(item.EntityId))
                ))
            .ToListAsync(HttpContext.RequestAborted);

        string? Pick(
            string entityType,
            int entityId,
            string fieldName)
        {
            string? Find(string? culture) =>
                string.IsNullOrWhiteSpace(culture)
                    ? null
                    : values.FirstOrDefault(item =>
                        item.EntityType == entityType &&
                        item.EntityId == entityId &&
                        item.FieldName.Equals(
                            fieldName,
                            StringComparison.OrdinalIgnoreCase) &&
                        item.CultureCode.Equals(
                            culture,
                            StringComparison.OrdinalIgnoreCase))
                        ?.Value;

            return Find(preferred) ?? Find(fallback);
        }

        string? EmployeeFullName(int employeeId)
        {
            var fullName = Pick(
                "Employee",
                employeeId,
                "FullName");

            if (!string.IsNullOrWhiteSpace(fullName))
            {
                return fullName;
            }

            var parts = new[]
            {
                Pick("Employee", employeeId, "FirstName"),
                Pick("Employee", employeeId, "SecondName"),
                Pick("Employee", employeeId, "ThirdName"),
                Pick("Employee", employeeId, "LastName")
            };

            var composed = string.Join(
                " ",
                parts
                    .Where(part => !string.IsNullOrWhiteSpace(part))
                    .Select(part => part!.Trim()));

            return string.IsNullOrWhiteSpace(composed)
                ? null
                : composed;
        }

        employee.FullName =
            EmployeeFullName(employee.Id) ??
            employee.FullName;

        employee.CompanyName =
            Pick(
                "Company",
                employee.CompanyId,
                "Name") ??
            employee.CompanyName;

        employee.BranchName =
            Pick(
                "Branch",
                employee.BranchId,
                "Name") ??
            employee.BranchName;

        employee.DepartmentName =
            Pick(
                "Department",
                employee.DepartmentId,
                "Name") ??
            employee.DepartmentName;

        if (employee.PositionId.HasValue)
        {
            employee.Position =
                Pick(
                    "Position",
                    employee.PositionId.Value,
                    "Name") ??
                employee.Position;
        }

        if (employee.DirectManagerId.HasValue)
        {
            employee.DirectManager =
                EmployeeFullName(
                    employee.DirectManagerId.Value) ??
                employee.DirectManager;
        }
    }
    private async Task LoadAttendanceAsync(int employeeId)
    {
        // نفس حارس بقية قارئي اليوميات (`MonthAttendanceStore` · `WeekAttendanceStore`
        // · `DashboardWidgetStore`): الجدول يُنشأ كسولاً، وبلا هذا يفشل الملفّ كلّه
        // بقاعدةٍ لم يُشغَّل عليها التحليل بعد.
        await DayAttendanceStore.EnsureAsync(_dbContext);
        await MissingPunchRequestStore.EnsureAsync(_dbContext);

        AttendanceCount = await CountAsync(
            @"SELECT COUNT(*) FROM DayAttendances WHERE EmployeeId = @EmployeeId AND WorkDate BETWEEN @FromDate AND @ToDate",
            employeeId);

        // ⚠️ هذه العدّادات الثلاثة كانت تُحسب من `AttendanceRecords.Status` — وهو
        // عمود **صامّ**: كل مسار كتابةٍ يختمه `Present` ثابتاً (الاستيراد الدفعي
        // بـ`AttendanceImportService` يمرّر `(int)AttendanceStatus.Present`، والبصمة
        // الأونلاين بـ`OnlinePunchStore` تكتب `Status` بقيمة `1` نصّاً بالـSQL).
        // ⟹ «التأخير» و«الغياب» كانا **صفراً دائماً** بملف كل موظف، و«الحضور»
        // يساوي عدد أيام البصم لا عدد أيام الحضور المحسوبة.
        //
        // ولم يكن العطل عرضياً: `AbsentCount` يغذّي `PayrollRiskItems` ومؤشّر
        // الصحة أدناه (`score -= AbsentCount * 8`) — فكان النظام يمنح كل موظفٍ
        // درجةً كاملة لأن مصدر الخصم صفر بنيوياً.
        //
        // المصدر الصحيح `DayAttendances`: اليوميات المحلَّلة — نفس ما تعرضه
        // `/DayAttendance` ونفس ما يقرأه المسير عبر `AttendanceSalaryLink`، فتتّحد
        // الحقيقة بدل حقيقتين متناقضتين بالتطبيق نفسه.
        PresentCount = await CountAsync(
            @"SELECT COUNT(*) FROM DayAttendances WHERE EmployeeId = @EmployeeId AND WorkDate BETWEEN @FromDate AND @ToDate AND Status = N'Present'",
            employeeId);

        LateCount = await CountAsync(
            @"SELECT COUNT(*) FROM DayAttendances WHERE EmployeeId = @EmployeeId AND WorkDate BETWEEN @FromDate AND @ToDate AND Status = N'Late'",
            employeeId);

        AbsentCount = await CountAsync(
            @"SELECT COUNT(*) FROM DayAttendances WHERE EmployeeId = @EmployeeId AND WorkDate BETWEEN @FromDate AND @ToDate AND Status = N'Absent'",
            employeeId);

        AnalyzedDays = await CountAsync(
            @"SELECT COUNT(*) FROM DayAttendances WHERE EmployeeId = @EmployeeId AND WorkDate BETWEEN @FromDate AND @ToDate",
            employeeId);

        MissingCheckoutCount = await CountAsync(
            @"SELECT COUNT(*) FROM DayAttendances WHERE EmployeeId = @EmployeeId AND WorkDate BETWEEN @FromDate AND @ToDate AND Status = N'Incomplete'",
            employeeId);

        TotalWorkingMinutes = await CountAsync(
            @"SELECT ISNULL(CAST(ROUND(SUM(WorkedHours) * 60, 0) AS int), 0)
              FROM DayAttendances
              WHERE EmployeeId = @EmployeeId AND WorkDate BETWEEN @FromDate AND @ToDate",
            employeeId);

        AttendanceRows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT
    da.WorkDate AS AttendanceDate,
    CASE WHEN raw.RawCount > 0 THEN raw.RawCheckIn ELSE da.CheckIn END AS CheckIn,
    CASE WHEN raw.RawCount > 0 THEN raw.RawCheckOut ELSE da.CheckOut END AS CheckOut,
    CASE
        WHEN da.Status = N'Incomplete' OR ((da.CheckIn IS NULL AND da.CheckOut IS NOT NULL) OR (da.CheckIn IS NOT NULL AND da.CheckOut IS NULL)) THEN 6
        WHEN da.Status = N'Present' THEN 1
        WHEN da.Status = N'Late' THEN 2
        WHEN da.Status = N'Absent' THEN 3
        WHEN da.Status IN (N'Leave', N'LeaveUnpaid') THEN 4
        WHEN da.Status = N'Weekend' THEN 5
        WHEN da.Status = N'Rest' THEN 7
        WHEN da.Status = N'Holiday' THEN 8
        ELSE 0
    END AS Status,
    ISNULL(firstRaw.Source, 0) AS Source,
    ISNULL(raw.IsAdjusted, 0) AS IsAdjusted,
    ISNULL(dv.Name, '') AS DeviceName,
    ISNULL(st.Name, '') AS ShiftName,
    ISNULL(da.DayKind, '') AS DayKind,
    ISNULL(pendingPunch.RefNo, '') AS PendingPunchRef
FROM DayAttendances da
OUTER APPLY (
    SELECT MIN(ar.CheckIn) AS RawCheckIn, MAX(ar.CheckOut) AS RawCheckOut,
           COUNT(*) AS RawCount, MAX(CASE WHEN ar.Source = 3 THEN 1 ELSE 0 END) AS IsAdjusted
    FROM AttendanceRecords ar
    WHERE ar.EmployeeId = da.EmployeeId
      AND ar.AttendanceDate = da.WorkDate
      AND ar.IsDeleted = 0
) raw
OUTER APPLY (
    SELECT TOP 1 ar.Source, ar.DeviceId
    FROM AttendanceRecords ar
    WHERE ar.EmployeeId = da.EmployeeId
      AND ar.AttendanceDate = da.WorkDate
      AND ar.IsDeleted = 0
    ORDER BY ar.CheckIn
) firstRaw
LEFT JOIN Devices dv ON firstRaw.DeviceId = dv.Id
LEFT JOIN ShiftTypes st ON st.Id = da.ShiftTypeId
OUTER APPLY (
    SELECT TOP 1 r.RefNo
    FROM MissingPunchRequests r
    WHERE r.EmployeeId = da.EmployeeId
      AND CAST(r.PunchAt AS date) = da.WorkDate
      AND r.Status = N'Pending'
      AND ISNULL(r.IsDeleted, 0) = 0
    ORDER BY r.CreatedAt DESC
) pendingPunch
WHERE da.EmployeeId = @EmployeeId
  AND da.WorkDate BETWEEN @FromDate AND @ToDate
ORDER BY da.WorkDate DESC;",
            command =>
            {
                AddEmployeeDateParameters(command, employeeId);
            },
            reader => new AttendanceRow
            {
                AttendanceDate = HrmsDatabase.GetDateOnly(reader, "AttendanceDate"),
                CheckIn = HrmsDatabase.GetDateTime(reader, "CheckIn"),
                CheckOut = HrmsDatabase.GetDateTime(reader, "CheckOut"),
                Status = HrmsDatabase.GetInt(reader, "Status"),
                Source = HrmsDatabase.GetInt(reader, "Source"),
                IsAdjusted = HrmsDatabase.GetBool(reader, "IsAdjusted"),
                DeviceName = HrmsDatabase.GetString(reader, "DeviceName"),
                ShiftName = HrmsDatabase.GetString(reader, "ShiftName"),
                DayKind = HrmsDatabase.GetString(reader, "DayKind"),
                PendingPunchRef = HrmsDatabase.GetString(reader, "PendingPunchRef")
            });
    }

    private async Task LoadDayRequestOptionsAsync(int employeeId)
    {
        await RequestTypeStore.EnsureAsync(_dbContext);
        DayShiftOptions = (await ShiftTypeStore.ListAsync(_dbContext))
            .Where(shift => shift.IsActive)
            .Select(shift => new DayShiftOption { Id = shift.Id, Name = shift.Name })
            .OrderBy(shift => shift.Name)
            .ToList();
        var categories = await RequestTypeStore.ListCategoriesAsync(_dbContext, onlyActive: true);
        var activeCategoryIds = categories.Select(category => category.Id).ToHashSet();
        var types = (await RequestTypeStore.ListTypesAsync(_dbContext, onlyActive: true))
            .Where(type => activeCategoryIds.Contains(type.CategoryId))
            .ToList();

        if (types.Any(type => !string.IsNullOrWhiteSpace(type.ConditionsJson)))
        {
            var rows = await HrConditionFacts.LoadAsync(_dbContext, employeeId);
            if (rows.Count > 0)
            {
                var facts = HrConditionFacts.Build(rows[0], DateOnly.FromDateTime(DateTime.Today));
                types = types.Where(type => HrConditions.Matches(
                        HrConditions.Deserialize(type.ConditionsJson), facts, matchWhenEmpty: true))
                    .ToList();
            }
        }

        DayRequestOptions = types.Select(type => new DayRequestOption
        {
            Value = $"type:{type.Id}",
            RequestTypeId = type.Id,
            RequestType = type.Name,
            Label = type.Name,
            Group = type.CategoryName,
            NeedsTime = type.NeedsTime,
            AttachmentRequired = type.AttachmentRequired,
            AttachmentLabel = type.AttachmentLabel ?? string.Empty,
            NeedsShift = false
        }).ToList();
        var representedKeys = DayRequestOptions
            .Select(option => ApprovalWorkflowEngine.ResolveRequestTypeKey(option.RequestType))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var supportedStandardKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LeaveRequest", "ExitPermission", "ShiftRequest", "ShiftChange",
            "WorkFromHome", "Overtime"
        };

        foreach (var definition in ApprovalTemplateStore.RequestTypes
                     .Where(def => supportedStandardKeys.Contains(def.Key)))
        {
            if (representedKeys.Contains(definition.Key)) continue;
            DayRequestOptions.Add(new DayRequestOption
            {
                Value = $"standard:{definition.Key}",
                RequestType = definition.Label,
                Label = definition.Label,
                Group = definition.Module,
                NeedsTime = definition.Key is "ExitPermission" or "Overtime",
                NeedsShift = definition.Key is "ShiftRequest" or "ShiftChange"
            });
            representedKeys.Add(definition.Key);
        }
    }

    private async Task LoadRequestsAsync(int employeeId)
    {
        var selfServiceRows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT TOP 25 Id, RequestType, RequestDate, FromDate, ToDate,
    CONVERT(varchar(5), StartTime, 108) AS StartTimeText,
    CONVERT(varchar(5), EndTime, 108) AS EndTimeText,
    ISNULL(Status, '') AS Status, ISNULL(CurrentStep, '') AS CurrentStep,
    ISNULL(Reason, '') AS Reason, CreatedAt
FROM SelfServiceRequests
WHERE EmployeeId = @EmployeeId
ORDER BY CreatedAt DESC;",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new RequestRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                RequestType = HrmsDatabase.GetString(reader, "RequestType"),
                RequestDate = HrmsDatabase.GetDateOnly(reader, "RequestDate"),
                FromDate = HrmsDatabase.GetDateOnly(reader, "FromDate"),
                ToDate = HrmsDatabase.GetDateOnly(reader, "ToDate"),
                StartTime = HrmsDatabase.GetString(reader, "StartTimeText"),
                EndTime = HrmsDatabase.GetString(reader, "EndTimeText"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                CurrentStep = HrmsDatabase.GetString(reader, "CurrentStep"),                Reason = HrmsDatabase.GetString(reader, "Reason"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });

        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var punchRequests = await MissingPunchRequestStore.ListAsync(
            _dbContext, scope, new MissingPunchRequestStore.Filter { EmployeeId = employeeId });
        var punchRows = punchRequests.Select(request => new RequestRow
        {
            Id = request.Id,
            RequestType = "بصمة مفقودة",
            RequestDate = DateOnly.FromDateTime(request.PunchAt),
            FromDate = DateOnly.FromDateTime(request.PunchAt),
            ToDate = DateOnly.FromDateTime(request.PunchAt),
            StartTime = request.PunchAt.ToString("HH:mm"),
            Status = request.Status,
            CurrentStep = request.IsPending ? "مراجعة الحضور" : MissingPunchRequestStore.StatusLabel(request.Status),
            Reason = request.Reason ?? string.Empty,
            CreatedAt = request.CreatedAt,
            Reference = request.RefNo,
            ReviewUrl = $"/MissingPunchRequests?Search={Uri.EscapeDataString(request.RefNo)}"
        }).ToList();

        RequestRows = selfServiceRows.Concat(punchRows)
            .OrderByDescending(row => row.CreatedAt)
            .Take(25)
            .ToList();
        PendingRequests = await CountRequestAsync(employeeId, "Pending") +
                          punchRows.Count(row => row.Status == MissingPunchRequestStore.Pending);
        ApprovedRequests = await CountRequestAsync(employeeId, "Approved") +
                           punchRows.Count(row => row.Status == MissingPunchRequestStore.Approved);
        RejectedRequests = await CountRequestAsync(employeeId, "Rejected") +
                           punchRows.Count(row => row.Status == MissingPunchRequestStore.Rejected);
    }

    private async Task LoadDocumentsAsync(int employeeId)
    {
        DocumentRows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT TOP 25
    Id,
    DocumentType,
    FileName,
    StoredPath,
    ExpiryDate,
    ISNULL(Notes, '') AS Notes,
    UploadedAt,
    ISNULL(UploadedBy, '') AS UploadedBy
FROM EmployeeDocuments
WHERE EmployeeId = @EmployeeId
ORDER BY UploadedAt DESC;",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new DocumentRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                DocumentType = HrmsDatabase.GetString(reader, "DocumentType"),
                FileName = HrmsDatabase.GetString(reader, "FileName"),
                StoredPath = HrmsDatabase.GetString(reader, "StoredPath"),
                ExpiryDate = HrmsDatabase.GetDateOnly(reader, "ExpiryDate"),
                Notes = HrmsDatabase.GetString(reader, "Notes"),
                UploadedAt = HrmsDatabase.GetDateTime(reader, "UploadedAt"),
                UploadedBy = HrmsDatabase.GetString(reader, "UploadedBy")
            });
    }

    private async Task LoadShiftsAsync(int employeeId)
    {
        ShiftRows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT TOP 15
    es.Id,
    s.Code,
    s.Name,
    CONVERT(varchar(5), s.StartTime, 108) AS StartTimeText,
    CONVERT(varchar(5), s.EndTime, 108) AS EndTimeText,
    s.WorkingHours,
    es.EffectiveFrom,
    es.EffectiveTo,
    es.IsCurrent,
    ISNULL(es.WeeklyOffDays, '') AS WeeklyOffDays
FROM EmployeeShifts es
INNER JOIN Shifts s ON es.ShiftId = s.Id
WHERE es.EmployeeId = @EmployeeId
ORDER BY es.IsCurrent DESC, es.EffectiveFrom DESC;",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new ShiftRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Code = HrmsDatabase.GetString(reader, "Code"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                StartTime = HrmsDatabase.GetString(reader, "StartTimeText"),
                EndTime = HrmsDatabase.GetString(reader, "EndTimeText"),
                WorkingHours = HrmsDatabase.GetString(reader, "WorkingHours"),
                EffectiveFrom = HrmsDatabase.GetDateOnly(reader, "EffectiveFrom"),
                EffectiveTo = HrmsDatabase.GetDateOnly(reader, "EffectiveTo"),
                IsCurrent = HrmsDatabase.GetBool(reader, "IsCurrent"),
                WeeklyOffDays = HrmsDatabase.GetString(reader, "WeeklyOffDays")
            });
    }

    private async Task LoadAuditAsync(int employeeId)
    {
        AuditTotalCount = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            @"SELECT COUNT(*) FROM AuditLogs WHERE EntityName = 'Employee' AND EntityId = CAST(@EmployeeId AS nvarchar(80));",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId));

        AuditPage = NormalizeActivityPage(AuditPage, AuditTotalCount);
        var offset = (AuditPage - 1) * ActivityPageSize;

        AuditRows = await HrmsDatabase.QueryAsync(
            _dbContext,
            @"
SELECT EntityName, EntityId, Action,
       ISNULL(OldValues, '') AS OldValues,
       ISNULL(NewValues, '') AS NewValues,
       ISNULL(UserName, '') AS UserName,
       ISNULL(IpAddress, '') AS IpAddress,
       CreatedAt
FROM AuditLogs
WHERE EntityName = 'Employee'
  AND EntityId = CAST(@EmployeeId AS nvarchar(80))
ORDER BY CreatedAt DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Offset", offset);
                HrmsDatabase.AddParameter(command, "@PageSize", ActivityPageSize);
            },
            reader => new AuditRow
            {
                EntityName = HrmsDatabase.GetString(reader, "EntityName"),
                EntityId = HrmsDatabase.GetString(reader, "EntityId"),
                Action = HrmsDatabase.GetString(reader, "Action"),
                OldValues = HrmsDatabase.GetString(reader, "OldValues"),
                NewValues = HrmsDatabase.GetString(reader, "NewValues"),
                UserName = HrmsDatabase.GetString(reader, "UserName"),
                IpAddress = HrmsDatabase.GetString(reader, "IpAddress"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });
    }

    private int NormalizeActivityPage(int page, int totalCount)
    {
        var totalPages = Math.Max(1, (totalCount + ActivityPageSize - 1) / ActivityPageSize);
        return Math.Clamp(page, 1, totalPages);
    }

    private async Task<int> CountAsync(string sql, int employeeId)
    {
        return await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            sql,
            command => AddEmployeeDateParameters(command, employeeId));
    }

    private async Task<int> CountRequestAsync(int employeeId, string status)
    {
        return await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            @"SELECT COUNT(*) FROM SelfServiceRequests WHERE EmployeeId = @EmployeeId AND Status = @Status",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Status", status);
            });
    }

    private void AddEmployeeDateParameters(System.Data.Common.DbCommand command, int employeeId)
    {
        HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
        HrmsDatabase.AddParameter(command, "@FromDate", FromDate);
        HrmsDatabase.AddParameter(command, "@ToDate", ToDate);
    }

    public string Display(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    public string DisplayDate(DateOnly? value)
    {
        return value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "-";
    }

    public string DisplayDayName(DateOnly? value)
    {
        if (!value.HasValue) return "-";
        return value.Value.DayOfWeek switch
        {
            DayOfWeek.Saturday => "السبت", DayOfWeek.Sunday => "الأحد", DayOfWeek.Monday => "الإثنين",
            DayOfWeek.Tuesday => "الثلاثاء", DayOfWeek.Wednesday => "الأربعاء", DayOfWeek.Thursday => "الخميس",
            DayOfWeek.Friday => "الجمعة", _ => "-"
        };
    }

    public string DayKindText(string? value)
    {
        return value switch
        {
            "Work" => "يوم عمل",
            "Weekend" => "عطلة نهاية الأسبوع",
            "Rest" => "يوم راحة",
            "Holiday" => "عطلة رسمية",
            "Remote" => "عمل عن بُعد",
            "BusinessTrip" => "رحلة عمل",
            _ => Display(value)
        };
    }

    public string DisplayDateTime(DateTime? value)
    {
        return value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm") : "-";
    }

    public string DisplayTime(DateTime? value)
    {
        return value.HasValue ? value.Value.ToString("HH:mm") : "-";
    }

    public string StatusText(int status)
    {
        return status switch
        {
            1 => "حاضر",
            2 => "متأخر",
            3 => "غائب",
            4 => "إجازة",
            5 => "عطلة نهاية الأسبوع",
            6 => "بصمة ناقصة",
            7 => "يوم راحة",
            8 => "عطلة رسمية",
            _ => "غير محدد"
        };
    }

    public string RequestStatusText(string? status)
    {
        return status switch
        {
            "Pending" => "معلق",
            "Approved" => "مقبول",
            "Rejected" => "مرفوض",
            _ => Display(status)
        };
    }

    public decimal TotalWorkingHours => Math.Round(TotalWorkingMinutes / 60m, 2);

    public int PayrollRiskItems => AbsentCount + MissingCheckoutCount + PendingRequests + ExpiredDocumentsCount + ExpiringDocumentsCount;

    public int ExpiredDocumentsCount => DocumentRows.Count(x => x.ExpiryDate.HasValue && x.ExpiryDate.Value < DateOnly.FromDateTime(DateTime.Today));

    public int ExpiringDocumentsCount => DocumentRows.Count(x =>
        x.ExpiryDate.HasValue &&
        x.ExpiryDate.Value >= DateOnly.FromDateTime(DateTime.Today) &&
        x.ExpiryDate.Value <= DateOnly.FromDateTime(DateTime.Today.AddDays(30)));

    public int AttendanceExceptionsCount => AttendanceRows.Count(x =>
        x.Status == 2 || x.Status == 3 || x.Status == 6);

    public int Employee360HealthScore
    {
        get
        {
            var score = 100;

            score -= AbsentCount * 8;
            score -= MissingCheckoutCount * 10;
            score -= PendingRequests * 6;
            score -= ExpiredDocumentsCount * 12;
            score -= ExpiringDocumentsCount * 4;

            if (score < 0)
            {
                return 0;
            }

            return score;
        }
    }

    public string Employee360HealthText => Employee360HealthScore >= 85
        ? "مستقر"
        : Employee360HealthScore >= 60
            ? "يحتاج متابعة"
            : "خطر تشغيلي";


    private void BuildProfileIntelligence()
    {
        if (Employee is null)
        {
            ProfileIntelligence = null;
            return;
        }

        var recordFacts = FileRecords
            .Where(record => !record.IsDeleted)
            .Select(record => new EmployeeProfileRecordFact(
                record.RecordType,
                record.Title,
                record.Subtitle,
                record.FromDate,
                record.ToDate,
                record.IsCurrent))
            .ToList();

        ProfileIntelligence = EmployeeProfileIntelligence.Evaluate(
            new EmployeeProfileIntelligenceInput(
                Employee.EmployeeNo,
                Employee.FullName,
                Employee.IsCitizen,
                Employee.NationalId,
                Employee.PassportNo,
                Employee.BirthDate,
                Employee.Nationality,
                Employee.Gender,
                Employee.CompanyName,
                Employee.BranchName,
                Employee.DepartmentName,
                Employee.Position,
                Employee.HireDate,
                Employee.WorkType,
                Employee.EmploymentStatus,
                Employee.Phone,
                Employee.Email,
                Employee.PersonalEmail,
                recordFacts));
    }

    public class EmployeeProfileCard
    {
        public int Id { get; set; }


        public int CompanyId { get; set; }

        public int BranchId { get; set; }

        public int DepartmentId { get; set; }

        public int? PositionId { get; set; }

        public int? DirectManagerId { get; set; }
public string EmployeeNo { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string NationalId { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public DateOnly? HireDate { get; set; }

        public DateOnly? BirthDate { get; set; }


        public string MaritalStatus { get; set; } = string.Empty;
        public bool IsActive { get; set; }

        public string Position { get; set; } = string.Empty;

        public string PhotoPath { get; set; } = string.Empty;

        public string Gender { get; set; } = string.Empty;

        public string Nationality { get; set; } = string.Empty;

        public string Country { get; set; } = string.Empty;

        public string ContractType { get; set; } = string.Empty;

        public DateOnly? ContractEndDate { get; set; }

        public bool IsCitizen { get; set; }
        public string PassportNo { get; set; } = string.Empty;
        public string SponsorName { get; set; } = string.Empty;
        public string Religion { get; set; } = string.Empty;
        public string PersonalEmail { get; set; } = string.Empty;
        public string PhoneExtension { get; set; } = string.Empty;
        public DateOnly? JoiningDate { get; set; }
        public string WorkType { get; set; } = string.Empty;
        public string JobGrade { get; set; } = string.Empty;

        public string EmploymentStatus { get; set; } = string.Empty;

        public string DepartmentName { get; set; } = string.Empty;

        public string BranchName { get; set; } = string.Empty;

        public string CompanyName { get; set; } = string.Empty;

        public string DirectManager { get; set; } = string.Empty;
    }

    public class AttendanceRow
    {
        public DateOnly? AttendanceDate { get; set; }

        public DateTime? CheckIn { get; set; }

        public DateTime? CheckOut { get; set; }

        public int Status { get; set; }

        public int Source { get; set; }

        public bool IsAdjusted { get; set; }

        public string DeviceName { get; set; } = string.Empty;

        public string ShiftName { get; set; } = string.Empty;

        public string DayKind { get; set; } = string.Empty;

        public string PendingPunchRef { get; set; } = string.Empty;
    }

    public class DayRequestOption
    {
        public string Value { get; set; } = string.Empty;
        public int? RequestTypeId { get; set; }
        public string RequestType { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public bool NeedsTime { get; set; }
        public bool NeedsShift { get; set; }
        public bool AttachmentRequired { get; set; }
        public string AttachmentLabel { get; set; } = string.Empty;
    }

    public class DayShiftOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class RequestRow
    {
        public int Id { get; set; }

        public string RequestType { get; set; } = string.Empty;

        public DateOnly? RequestDate { get; set; }

        public DateOnly? FromDate { get; set; }

        public DateOnly? ToDate { get; set; }

        public string StartTime { get; set; } = string.Empty;

        public string EndTime { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string CurrentStep { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public DateTime? CreatedAt { get; set; }

        public string Reference { get; set; } = string.Empty;

        public string ReviewUrl { get; set; } = string.Empty;
    }

    public class DocumentRow
    {
        public int Id { get; set; }

        public string DocumentType { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string StoredPath { get; set; } = string.Empty;

        public DateOnly? ExpiryDate { get; set; }

        public string Notes { get; set; } = string.Empty;

        public DateTime? UploadedAt { get; set; }

        public string UploadedBy { get; set; } = string.Empty;
    }

    public class ShiftRow
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string StartTime { get; set; } = string.Empty;

        public string EndTime { get; set; } = string.Empty;

        public string WorkingHours { get; set; } = string.Empty;

        public DateOnly? EffectiveFrom { get; set; }

        public DateOnly? EffectiveTo { get; set; }

        public bool IsCurrent { get; set; }

        public string WeeklyOffDays { get; set; } = string.Empty;
    }

    public class AuditRow
    {
        public string EntityName { get; set; } = string.Empty;

        public string EntityId { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public string OldValues { get; set; } = string.Empty;

        public string NewValues { get; set; } = string.Empty;

        public string UserName { get; set; } = string.Empty;

        public string IpAddress { get; set; } = string.Empty;

        public DateTime? CreatedAt { get; set; }
    }
}
