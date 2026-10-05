using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Application.Announcements.Services;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Api;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Controllers.Api;

/// <summary>
/// واجهة الموظف بالموبايل (الأساسيات): ملف الموظف، الحضور الأخير، رصيد الإجازات،
/// الطلبات (عرض/تقديم)، البصم الذاتي، وطلب البصمة المفقودة. كلها مقيّدة بالموظف
/// صاحب التوكن. تعيد استخدام مخازن HRMS نفسها المستخدمة بالبوابة.
/// </summary>
[ApiController]
[Route("api/v1/me")]
[Route("api/me")]
[Authorize(AuthenticationSchemes = ApiTokenAuthHandler.SchemeName)]
public sealed class MeController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IProtectedFileService _protectedFiles;
    private readonly IAnnouncementService _announcements;
    private readonly IWebHostEnvironment _environment;
    private readonly ICompanyScopeProvider _companyScope;

    public MeController(
        ApplicationDbContext db,
        IProtectedFileService protectedFiles,
        IAnnouncementService announcements,
        IWebHostEnvironment environment,
        ICompanyScopeProvider companyScope)
    {
        _db = db;
        _protectedFiles = protectedFiles;
        _announcements = announcements;
        _environment = environment;
        _companyScope = companyScope;
    }

    private int EmployeeId =>
        int.TryParse(User.FindFirst("EmployeeId")?.Value, out var id) ? id : 0;

    private IActionResult? RequireEmployee() =>
        EmployeeId <= 0 ? BadRequest(new { message = "الحساب غير مرتبط بموظف." }) : null;

    private string ActorName =>
        User?.Identity?.Name ?? "employee";

    private string[] ActorRoles =>
        User.Claims
            .Where(claim => claim.Type == System.Security.Claims.ClaimTypes.Role)
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// نطاق الموافقات يلتزم بنطاق People إن وُجد. المدير الذي لا يملك نطاق
    /// People عاماً لا يُفتح له النظام؛ يقتصر فقط على شركة ملفه، ثم يحسم
    /// ApprovalWorkflowEngine هل يملك الخطوة الحالية فعلاً.
    /// </summary>
    private async Task<CompanyScope> ApprovalScopeAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (!scope.IsDeniedAll) return scope;

        if (EmployeeId <= 0) return CompanyScope.DeniedAll();

        var companyIds = await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT CompanyId
FROM Employees
WHERE Id = @Id
  AND ISNULL(IsDeleted, 0) = 0
  AND CompanyId IS NOT NULL;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", EmployeeId),
            reader => HrmsDatabase.GetNullableInt(reader, "CompanyId"));

        var companyId = companyIds.SingleOrDefault();
        return companyId is > 0
            ? CompanyScope.ForCompanies([companyId.Value])
            : CompanyScope.DeniedAll();
    }

    /// <summary>ملف الموظف الحالي كما يحتاجه تطبيق الموظف.</summary>
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        if (RequireEmployee() is { } bad) return bad;

        var row = (await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT TOP 1
       e.EmployeeNo,
       e.FullName,
       ISNULL(e.Position, N'') AS Position,
       ISNULL((
           SELECT TOP 1 lev.Value
           FROM LocalizedEntityValues lev
           WHERE lev.CompanyId = e.CompanyId
             AND lev.EntityType = N'Position'
             AND lev.EntityId = e.PositionId
             AND lev.FieldName = N'Name'
             AND lev.CultureCode = N'en-US'
             AND ISNULL(lev.IsDeleted, 0) = 0
           ORDER BY lev.Id DESC
       ), N'') AS PositionEn,
       ISNULL(d.Name, N'') AS Department,
       ISNULL((
           SELECT TOP 1 lev.Value
           FROM LocalizedEntityValues lev
           WHERE lev.CompanyId = e.CompanyId
             AND lev.EntityType = N'Department'
             AND lev.EntityId = e.DepartmentId
             AND lev.FieldName = N'Name'
             AND lev.CultureCode = N'en-US'
             AND ISNULL(lev.IsDeleted, 0) = 0
           ORDER BY lev.Id DESC
       ), N'') AS DepartmentEn,
       ISNULL(b.Name, N'') AS Branch,
       ISNULL((
           SELECT TOP 1 lev.Value
           FROM LocalizedEntityValues lev
           WHERE lev.CompanyId = e.CompanyId
             AND lev.EntityType = N'Branch'
             AND lev.EntityId = e.BranchId
             AND lev.FieldName = N'Name'
             AND lev.CultureCode = N'en-US'
             AND ISNULL(lev.IsDeleted, 0) = 0
           ORDER BY lev.Id DESC
       ), N'') AS BranchEn,
       ISNULL(e.Phone, N'') AS Phone,
       ISNULL(e.Email, N'') AS Email,
       ISNULL(e.PersonalEmail, N'') AS PersonalEmail,
       ISNULL(e.NationalId, N'') AS NationalId,
       e.HireDate,
       e.JoiningDate,
       e.BirthDate,
       ISNULL(e.Country, N'') AS Country,
       ISNULL(e.Gender, N'') AS Gender,
       ISNULL(e.MaritalStatus, N'') AS MaritalStatus,
       ISNULL(e.Nationality, N'') AS Nationality,
       ISNULL(e.EmploymentStatus, N'') AS EmploymentStatus,
       ISNULL(e.WorkType, N'') AS WorkType,
       ISNULL(e.JobGrade, N'') AS JobGrade,
       ISNULL(e.FirstNameEn, N'') AS FirstNameEn,
       ISNULL(e.SecondNameEn, N'') AS SecondNameEn,
       ISNULL(e.ThirdNameEn, N'') AS ThirdNameEn,
       ISNULL(e.LastNameEn, N'') AS LastNameEn,
       ISNULL(e.PassportNo, N'') AS PassportNo,
       ISNULL(e.PhotoPath, N'') AS PhotoPath,
       ISNULL(e.IsActive,1) AS IsActive
FROM Employees e
LEFT JOIN Departments d ON d.Id = e.DepartmentId
LEFT JOIN Branches b ON b.Id = e.BranchId
WHERE e.Id = @Id
  AND ISNULL(e.IsDeleted, 0) = 0;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", EmployeeId),
            reader => new
            {
                employeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                fullName = HrmsDatabase.GetString(reader, "FullName"),
                position = HrmsDatabase.GetString(reader, "Position"),
                positionEn = HrmsDatabase.GetString(reader, "PositionEn"),
                department = HrmsDatabase.GetString(reader, "Department"),
                departmentEn = HrmsDatabase.GetString(reader, "DepartmentEn"),
                branch = HrmsDatabase.GetString(reader, "Branch"),
                branchEn = HrmsDatabase.GetString(reader, "BranchEn"),
                phone = HrmsDatabase.GetString(reader, "Phone"),
                email = HrmsDatabase.GetString(reader, "Email"),
                personalEmail = HrmsDatabase.GetString(reader, "PersonalEmail"),
                nationalId = HrmsDatabase.GetString(reader, "NationalId"),
                hireDate = HrmsDatabase.GetDateOnly(reader, "HireDate")?.ToString("yyyy-MM-dd"),
                joiningDate = HrmsDatabase.GetDateOnly(reader, "JoiningDate")?.ToString("yyyy-MM-dd"),
                birthDate = HrmsDatabase.GetDateOnly(reader, "BirthDate")?.ToString("yyyy-MM-dd"),
                country = HrmsDatabase.GetString(reader, "Country"),
                gender = HrmsDatabase.GetString(reader, "Gender"),
                maritalStatus = HrmsDatabase.GetString(reader, "MaritalStatus"),
                nationality = HrmsDatabase.GetString(reader, "Nationality"),
                employmentStatus = HrmsDatabase.GetString(reader, "EmploymentStatus"),
                workType = HrmsDatabase.GetString(reader, "WorkType"),
                jobGrade = HrmsDatabase.GetString(reader, "JobGrade"),
                firstNameEn = HrmsDatabase.GetString(reader, "FirstNameEn"),
                secondNameEn = HrmsDatabase.GetString(reader, "SecondNameEn"),
                thirdNameEn = HrmsDatabase.GetString(reader, "ThirdNameEn"),
                lastNameEn = HrmsDatabase.GetString(reader, "LastNameEn"),
                passportNo = HrmsDatabase.GetString(reader, "PassportNo"),
                photoPath = HrmsDatabase.GetString(reader, "PhotoPath"),
                isActive = HrmsDatabase.GetBool(reader, "IsActive")
            })).FirstOrDefault();

        if (row is null)
            return NotFound(new { message = "الموظف غير موجود." });

        return Ok(new
        {
            row.employeeNo,
            row.fullName,
            row.position,
            row.positionEn,
            row.department,
            row.departmentEn,
            row.branch,
            row.branchEn,
            row.phone,
            row.email,
            row.personalEmail,
            row.nationalId,
            row.hireDate,
            row.joiningDate,
            row.birthDate,
            row.country,
            row.gender,
            row.maritalStatus,
            row.nationality,
            row.employmentStatus,
            row.workType,
            row.jobGrade,
            row.firstNameEn,
            row.secondNameEn,
            row.thirdNameEn,
            row.lastNameEn,
            row.passportNo,
            hasPhoto = !string.IsNullOrWhiteSpace(row.photoPath),
            photoUrl = !string.IsNullOrWhiteSpace(row.photoPath)
                ? "/api/v1/me/photo"
                : null,
            row.isActive
        });
    }

    /// <summary>صورة الموظف الحالي عبر توكن الموبايل فقط.</summary>
    [HttpGet("photo")]
    public async Task<IActionResult> ProfilePhoto()
    {
        if (RequireEmployee() is { } bad) return bad;

        var storedPath = await HrmsDatabase.ScalarAsync<string>(
            _db,
            """
SELECT ISNULL(PhotoPath, N'')
FROM Employees
WHERE Id = @Id
  AND ISNULL(IsDeleted, 0) = 0;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", EmployeeId));

        if (string.IsNullOrWhiteSpace(storedPath) ||
            !storedPath.StartsWith("/uploads/employee-photos/", StringComparison.OrdinalIgnoreCase))
            return NotFound();

        var relative = storedPath.TrimStart('/');
        var webRoot = string.IsNullOrWhiteSpace(_environment.WebRootPath)
            ? Path.Combine(_environment.ContentRootPath, "wwwroot")
            : _environment.WebRootPath;

        if (!ProtectedFileStore.TryResolvePhysicalPath(webRoot, relative, out var physicalPath) ||
            !System.IO.File.Exists(physicalPath))
            return NotFound();

        var extension = Path.GetExtension(physicalPath).ToLowerInvariant();
        if (extension is not ".png" and not ".jpg" and not ".jpeg" and not ".webp")
            return NotFound();

        Response.Headers["Cache-Control"] = "private, no-store, max-age=0";
        Response.Headers["Pragma"] = "no-cache";

        return File(
            System.IO.File.OpenRead(physicalPath),
            ProtectedFileStore.ContentTypeFor(extension));
    }

    /// <summary>الموظفون المباشرون تحت إدارة صاحب التوكن مع ملخص حضور اليوم.</summary>
    [HttpGet("team")]
    public async Task<IActionResult> Team()
    {
        if (RequireEmployee() is { } bad) return bad;

        var rows = await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT
    e.Id,
    ISNULL(e.EmployeeNo, N'') AS EmployeeNo,
    ISNULL(e.FullName, N'') AS FullName,
    LTRIM(RTRIM(CONCAT(
        ISNULL(e.FirstNameEn,N''), N' ',
        ISNULL(e.SecondNameEn,N''), N' ',
        ISNULL(e.ThirdNameEn,N''), N' ',
        ISNULL(e.LastNameEn,N'')))) AS FullNameEn,
    ISNULL(e.Position, N'') AS Position,
    ISNULL((
        SELECT TOP 1 lev.Value
        FROM LocalizedEntityValues lev
        WHERE lev.CompanyId=e.CompanyId
          AND lev.EntityType=N'Position'
          AND lev.EntityId=e.PositionId
          AND lev.FieldName=N'Name'
          AND lev.CultureCode=N'en-US'
          AND ISNULL(lev.IsDeleted,0)=0
        ORDER BY lev.Id DESC
    ),N'') AS PositionEn,
    ISNULL(d.Name, N'') AS Department,
    ISNULL((
        SELECT TOP 1 lev.Value
        FROM LocalizedEntityValues lev
        WHERE lev.CompanyId=e.CompanyId
          AND lev.EntityType=N'Department'
          AND lev.EntityId=e.DepartmentId
          AND lev.FieldName=N'Name'
          AND lev.CultureCode=N'en-US'
          AND ISNULL(lev.IsDeleted,0)=0
        ORDER BY lev.Id DESC
    ),N'') AS DepartmentEn,
    ISNULL(b.Name, N'') AS Branch,
    ISNULL((
        SELECT TOP 1 lev.Value
        FROM LocalizedEntityValues lev
        WHERE lev.CompanyId=e.CompanyId
          AND lev.EntityType=N'Branch'
          AND lev.EntityId=e.BranchId
          AND lev.FieldName=N'Name'
          AND lev.CultureCode=N'en-US'
          AND ISNULL(lev.IsDeleted,0)=0
        ORDER BY lev.Id DESC
    ),N'') AS BranchEn,
    ISNULL(e.Email, N'') AS Email,
    ISNULL(e.Phone, N'') AS Phone,
    ISNULL(e.IsActive, 1) AS IsActive,
    punches.CheckIn,
    punches.CheckOut
FROM Employees e
INNER JOIN Employees manager
    ON manager.Id = @ManagerId
   AND ISNULL(manager.IsDeleted, 0) = 0
   AND e.CompanyId = manager.CompanyId
LEFT JOIN Departments d ON d.Id = e.DepartmentId
LEFT JOIN Branches b ON b.Id = e.BranchId
OUTER APPLY
(
    SELECT
        MIN(ar.CheckIn) AS CheckIn,
        MAX(ar.CheckOut) AS CheckOut
    FROM AttendanceRecords ar
    WHERE ar.EmployeeId = e.Id
      AND ISNULL(ar.IsDeleted, 0) = 0
      AND ar.AttendanceDate = CAST(GETDATE() AS date)
) punches
WHERE e.DirectManagerId = @ManagerId
  AND ISNULL(e.IsDeleted, 0) = 0
ORDER BY e.FullName;
""",
            command => HrmsDatabase.AddParameter(command, "@ManagerId", EmployeeId),
            reader => new
            {
                id = HrmsDatabase.GetInt(reader, "Id"),
                employeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                fullName = HrmsDatabase.GetString(reader, "FullName"),
                fullNameEn = HrmsDatabase.GetString(reader, "FullNameEn"),
                position = HrmsDatabase.GetString(reader, "Position"),
                positionEn = HrmsDatabase.GetString(reader, "PositionEn"),
                department = HrmsDatabase.GetString(reader, "Department"),
                departmentEn = HrmsDatabase.GetString(reader, "DepartmentEn"),
                branch = HrmsDatabase.GetString(reader, "Branch"),
                branchEn = HrmsDatabase.GetString(reader, "BranchEn"),
                email = HrmsDatabase.GetString(reader, "Email"),
                phone = HrmsDatabase.GetString(reader, "Phone"),
                isActive = HrmsDatabase.GetBool(reader, "IsActive"),
                checkIn = HrmsDatabase.GetDateTime(reader, "CheckIn")?.ToString("HH:mm"),
                checkOut = HrmsDatabase.GetDateTime(reader, "CheckOut")?.ToString("HH:mm")
            });

        return Ok(rows);
    }

    /// <summary>ملخص أحدث مستند هوية حالي من كل نوع للموظف.</summary>
    [HttpGet("identity-documents")]
    public async Task<IActionResult> IdentityDocuments()
    {
        if (RequireEmployee() is { } bad) return bad;

        var items = await HrmsDatabase.QueryAsync(
            _db,
            """
WITH ranked AS
(
    SELECT d.*,
           ROW_NUMBER() OVER
           (
               PARTITION BY d.DocumentType
               ORDER BY ISNULL(d.UpdatedAt, d.CreatedAt) DESC, d.Id DESC
           ) AS rn
    FROM EmployeeIdentityDocuments d
    WHERE d.EmployeeId = @EmployeeId
      AND ISNULL(d.IsCurrent, 1) = 1
)
SELECT DocumentType, CountryCode, DocumentNumber, NationalNumber,
       FamilyNumber, IssueDate, ExpiryDate, VerificationStatus,
       OriginalVerificationStatus
FROM ranked
WHERE rn = 1
ORDER BY DocumentType;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", EmployeeId),
            reader => new
            {
                documentType = HrmsDatabase.GetString(reader, "DocumentType"),
                countryCode = HrmsDatabase.GetString(reader, "CountryCode"),
                documentNumber = HrmsDatabase.GetString(reader, "DocumentNumber"),
                nationalNumber = HrmsDatabase.GetString(reader, "NationalNumber"),
                familyNumber = HrmsDatabase.GetString(reader, "FamilyNumber"),
                issueDate = HrmsDatabase.GetDateOnly(reader, "IssueDate")?.ToString("yyyy-MM-dd"),
                expiryDate = HrmsDatabase.GetDateOnly(reader, "ExpiryDate")?.ToString("yyyy-MM-dd"),
                verificationStatus = HrmsDatabase.GetString(reader, "VerificationStatus"),
                originalVerificationStatus = HrmsDatabase.GetString(reader, "OriginalVerificationStatus")
            });

        return Ok(items);
    }

    /// <summary>آخر إعلانات الموظف كما تظهر في بوابة الموظف.</summary>
    [HttpGet("announcements")]
    public async Task<IActionResult> Announcements([FromQuery] int take = 5)
    {
        if (RequireEmployee() is { } bad) return bad;
        take = Math.Clamp(take, 1, 20);

        var items = await _announcements.GetEmployeeFeedAsync(
            EmployeeId,
            HttpContext.RequestAborted);

        return Ok(items
            .Take(take)
            .Select(item => new
            {
                id = item.Id,
                title = item.Title,
                body = item.Body,
                category = item.Category,
                publishDate = item.PublishDate?.ToString("yyyy-MM-dd"),
                isRead = item.IsRead,
                firstReadAtUtc = item.FirstReadAtUtc
            }));
    }

    /// <summary>مفاتيح بصمة/وجه الحضور للموظف الحالي.</summary>
    [HttpGet("biometric-keys")]
    public async Task<IActionResult> BiometricKeys()
    {
        if (RequireEmployee() is { } bad) return bad;

        var items = await WebAuthnCredentialStore.ListForEmployeeAsync(_db, EmployeeId);
        return Ok(items.Select(item => new
        {
            id = item.Id,
            deviceLabel = item.DeviceLabel,
            status = item.Status,
            statusText = item.StatusText,
            createdAt = item.CreatedAt,
            approvedAt = item.ApprovedAt,
            lastUsedAt = item.LastUsedAt
        }));
    }

    /// <summary>بيانات الراتب الحالية وآخر احتساب Payroll للموظف الحالي فقط.</summary>
    [HttpGet("compensation")]
    public async Task<IActionResult> Compensation()
    {
        if (RequireEmployee() is { } bad) return bad;

        var financial = (await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT TOP 1
       ISNULL(BasicSalary, 0) AS BasicSalary,
       ISNULL(PaymentMethod, N'') AS PaymentMethod,
       ISNULL(BankName, N'') AS BankName,
       COALESCE(NULLIF(Iban, N''), NULLIF(CardNo, N''), NULLIF(MxpAccount, N''), N'') AS AccountReference,
       ISNULL(Currency, N'IQD') AS Currency
FROM EmployeeFinancialInfos
WHERE EmployeeId = @EmployeeId
  AND ISNULL(IsDeleted, 0) = 0
ORDER BY ISNULL(UpdatedAt, CreatedAt) DESC, Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", EmployeeId),
            reader => new
            {
                basicSalary = Convert.ToDecimal(reader["BasicSalary"]),
                paymentMethod = HrmsDatabase.GetString(reader, "PaymentMethod"),
                bankName = HrmsDatabase.GetString(reader, "BankName"),
                bankAccount = HrmsDatabase.GetString(reader, "AccountReference"),
                currency = HrmsDatabase.GetString(reader, "Currency")
            })).FirstOrDefault();

        var payroll = (await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT TOP 1
       l.RunId,
       r.[Year],
       r.[Month],
       ISNULL(r.Status, N'') AS PayrollStatus,
       ISNULL(l.TotalAllowances, 0) AS Allowances,
       ISNULL(l.TaxAmount, 0) AS TaxAmount,
       ISNULL(l.GosiEmployee, 0) AS GosiEmployee,
       ISNULL(l.OtherDeductions, 0) AS OtherDeductions,
       ISNULL(l.GrossSalary, 0) AS GrossSalary,
       ISNULL(l.NetSalary, 0) AS NetSalary,
       ISNULL(l.WorkDays, 0) AS WorkDays,
       ISNULL(l.AbsentDays, 0) AS AbsentDays,
       COALESCE(NULLIF(l.PayrollCurrency, N''), NULLIF(l.SourceCurrency, N''), N'IQD') AS PayrollCurrency
FROM PayrollRunLines l
JOIN PayrollRuns r ON r.Id = l.RunId
WHERE l.EmployeeId = @EmployeeId
ORDER BY r.[Year] DESC, r.[Month] DESC, l.Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", EmployeeId),
            reader => new
            {
                runId = HrmsDatabase.GetInt(reader, "RunId"),
                year = HrmsDatabase.GetInt(reader, "Year"),
                month = HrmsDatabase.GetInt(reader, "Month"),
                status = HrmsDatabase.GetString(reader, "PayrollStatus"),
                allowances = Convert.ToDecimal(reader["Allowances"]),
                taxAmount = Convert.ToDecimal(reader["TaxAmount"]),
                gosiEmployee = Convert.ToDecimal(reader["GosiEmployee"]),
                otherDeductions = Convert.ToDecimal(reader["OtherDeductions"]),
                grossSalary = Convert.ToDecimal(reader["GrossSalary"]),
                netSalary = Convert.ToDecimal(reader["NetSalary"]),
                workDays = Convert.ToDecimal(reader["WorkDays"]),
                absentDays = Convert.ToDecimal(reader["AbsentDays"]),
                currency = HrmsDatabase.GetString(reader, "PayrollCurrency")
            })).FirstOrDefault();

        if (financial is null && payroll is null)
        {
            return Ok(new
            {
                hasData = false,
                basicSalary = 0m,
                allowances = 0m,
                deductions = 0m,
                gross = 0m,
                net = 0m,
                taxAmount = 0m,
                gosiEmployee = 0m,
                otherDeductions = 0m,
                paymentMethod = "",
                bankName = "",
                bankAccount = "",
                currency = "IQD",
                payrollRunId = (int?)null,
                payrollYear = (int?)null,
                payrollMonth = (int?)null,
                payrollStatus = "",
                workDays = 0m,
                absentDays = 0m
            });
        }

        var deductions = payroll is null
            ? 0m
            : payroll.taxAmount + payroll.gosiEmployee + payroll.otherDeductions;

        return Ok(new
        {
            hasData = true,
            basicSalary = financial?.basicSalary ?? 0m,
            allowances = payroll?.allowances ?? 0m,
            deductions,
            gross = payroll?.grossSalary ?? financial?.basicSalary ?? 0m,
            net = payroll?.netSalary ?? financial?.basicSalary ?? 0m,
            taxAmount = payroll?.taxAmount ?? 0m,
            gosiEmployee = payroll?.gosiEmployee ?? 0m,
            otherDeductions = payroll?.otherDeductions ?? 0m,
            paymentMethod = financial?.paymentMethod ?? "",
            bankName = financial?.bankName ?? "",
            bankAccount = financial?.bankAccount ?? "",
            currency = !string.IsNullOrWhiteSpace(payroll?.currency)
                ? payroll.currency
                : financial?.currency ?? "IQD",
            payrollRunId = payroll?.runId,
            payrollYear = payroll?.year,
            payrollMonth = payroll?.month,
            payrollStatus = payroll?.status ?? "",
            workDays = payroll?.workDays ?? 0m,
            absentDays = payroll?.absentDays ?? 0m
        });
    }

    /// <summary>الحضور اليومي الأخير (من يوميات المحرك الرسمي).</summary>
    [HttpGet("attendance")]
    public async Task<IActionResult> Attendance([FromQuery] int days = 30)
    {
        if (RequireEmployee() is { } bad) return bad;
        days = Math.Clamp(days, 1, 90);
        var to = DateOnly.FromDateTime(DateTime.Today);
        var from = to.AddDays(-days);
        var mine = (await DayAttendanceStore.ListForEmployeeAsync(_db, EmployeeId, from, to))
            .OrderByDescending(r => r.WorkDate)
            .Select(r => new
            {
                date = r.WorkDate.ToString("yyyy-MM-dd"),
                status = DayAttendanceStore.StatusLabel(r.Status),
                checkIn = r.CheckIn?.ToString("HH:mm"),
                checkOut = r.CheckOut?.ToString("HH:mm"),
                lateHours = r.LateHours,
                workedHours = r.WorkedHours
            }).ToList();
        return Ok(mine);
    }

    /// <summary>أرصدة الإجازات/المغادرات ذات الرصيد من سياسة الشركة الحالية.</summary>
    [HttpGet("leave-balance")]
    public async Task<IActionResult> LeaveBalance()
    {
        if (RequireEmployee() is { } bad) return bad;
        var balances = await CompanyLeavePolicyStore.GetBalanceSnapshotsAsync(
            _db, EmployeeId, DateOnly.FromDateTime(DateTime.Today));

        await RequestTypeStore.EnsureAsync(_db);
        var requestTypes = (await RequestTypeStore.ListTypesAsync(_db))
            .ToDictionary(item => item.Id);

        return Ok(balances.Select(b =>
        {
            requestTypes.TryGetValue(b.SourceRequestTypeId, out var requestType);
            return new
            {
                requestTypeId = b.SourceRequestTypeId,
                type = b.RequestTypeName,
                typeEn = requestType?.NameEn,
                category = b.CategoryName,
                categoryEn = requestType?.CategoryNameEn,
                unit = b.Unit,
            entitled = b.Entitlement,
            approved = b.Approved,
            pendingReserved = b.PendingReserved,
            reserved = b.Reserved,
            used = b.Reserved,
                remaining = b.Remaining
            };
        }));
    }

    /// <summary>كتالوج أنواع الطلبات الفعّالة المسموح بها للمستخدم الحالي.</summary>
    [HttpGet("request-types")]
    public async Task<IActionResult> RequestTypes()
    {
        if (RequireEmployee() is { } bad) return bad;

        var eligibility = await EmployeeRequestEligibility.CheckAsync(
            _db, EmployeeId, HttpContext.RequestAborted);
        if (!eligibility.IsEligible)
            return Ok(new
            {
                eligible = false,
                message = eligibility.Message,
                canSubmitMissingPunch = false,
                items = Array.Empty<object>()
            });

        var canSubmitMissingPunch = await SelfServiceAccessPolicy.IsAllowedAsync(
            _db, HttpContext, "PunchCorrection");

        await RequestTypeStore.EnsureAsync(_db);
        var types = await RequestTypeStore.ListTypesAsync(_db, onlyActive: true);
        var companyId = await HrmsDatabase.ScalarAsync<int>(
            _db,
            "SELECT ISNULL(CompanyId,0) FROM Employees WHERE Id=@Id AND ISNULL(IsDeleted,0)=0;",
            command => HrmsDatabase.AddParameter(command, "@Id", EmployeeId));
        var policyByType = companyId > 0
            ? (await CompanyLeavePolicyStore.ListForCompanyAsync(_db, companyId, onlyActive: true))
                .ToDictionary(policy => policy.RequestTypeId)
            : new Dictionary<int, CompanyLeavePolicyStore.Policy>();
        var items = new List<object>();

        foreach (var type in types)
        {
            var action = ActionForRequestType(type);
            if (action is null ||
                !await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, action))
                continue;

            policyByType.TryGetValue(type.Id, out var policy);
            var attachmentRequired =
                policy?.AttachmentRequiredOverride ?? type.AttachmentRequired;

            items.Add(new
            {
                id = type.Id,
                name = type.Name,
                nameEn = type.NameEn,
                category = type.CategoryName,
                categoryEn = type.CategoryNameEn,
                needsTime = type.NeedsTime,
                attachmentRequired,
                attachmentLabel = type.AttachmentLabel,
                reasonRequired = policy?.ReasonRequired ?? false,
                allowedDays = type.AllowedDays,
                effectCode = type.EffectCode,
                hasBalance = type.HasBalance
            });
        }

        return Ok(new
        {
            eligible = true,
            message = (string?)null,
            canSubmitMissingPunch,
            items
        });
    }

    /// <summary>طلبات البصمة المفقودة الخاصة بي.</summary>
    [HttpGet("missing-punch")]
    public async Task<IActionResult> MyMissingPunches()
    {
        if (RequireEmployee() is { } bad) return bad;
        var rows = await MissingPunchRequestStore.ListAsync(_db, SmartAttendance.Web.Infrastructure.Security.CompanyScope.Unrestricted(), new MissingPunchRequestStore.Filter { EmployeeId = EmployeeId });
        return Ok(rows.Select(r => new
        {
            refNo = r.RefNo,
            punchAt = r.PunchAt.ToString("yyyy-MM-dd HH:mm"),
            punchType = r.PunchTypeText,
            status = r.StatusText,
            reason = r.Reason
        }));
    }

    public sealed record OnlinePunchRequest(
        string PunchType,
        double? Latitude = null,
        double? Longitude = null,
        string? BioToken = null);

    /// <summary>بصمة ذاتية (دخول/خروج) بوقت الخادم الحالي.</summary>
    [HttpPost("online-punch")]
    public async Task<IActionResult> OnlinePunch([FromBody] OnlinePunchRequest body)
    {
        if (RequireEmployee() is { } bad) return bad;
        var type = body?.PunchType == "Out" ? "Out" : "In";
        var now = DateTime.Now;
        var biometricVerified = WebAuthnProofStore.Consume(body?.BioToken, EmployeeId);
        var result = await OnlinePunchStore.RecordAsync(
            _db,
            EmployeeId,
            type,
            now,
            null,
            body?.Latitude,
            body?.Longitude,
            biometricVerified);
        return result.Status switch
        {
            OnlinePunchStore.PunchStatus.OutsideGeofence =>
                BadRequest(new { message = "رُفضت البصمة: أنت خارج نطاق موقع العمل المحدد لك (أو لم يصل موقعك)." }),
            OnlinePunchStore.PunchStatus.Recorded =>
                Ok(new { message = $"سُجّلت بصمة {(type == "Out" ? "الانصراف" : "الحضور")}.", at = now.ToString("yyyy-MM-dd HH:mm"), punchType = type }),
            OnlinePunchStore.PunchStatus.TooSoonForCheckout =>
                BadRequest(new { message = $"لا يمكن تسجيل الانصراف قبل مرور {OnlinePunchStore.FormatDuration(result.MinCheckoutHours)} من تسجيل الحضور — تبقّى {OnlinePunchStore.FormatDuration(result.HoursRemaining)}." }),
            OnlinePunchStore.PunchStatus.BiometricRequired =>
                BadRequest(new
                {
                    code = "BIOMETRIC_REQUIRED",
                    message = "يلزم تأكيد بصمة الوجه أو الأصبع قبل تسجيل الحضور."
                }),
            _ =>
                BadRequest(new { message = "تم تجاهل البصمة: سُجّلت بصمة مماثلة خلال أقل من دقيقة." })
        };
    }

    /// <summary>بصمات يوم الموظف مصنَّفةً بالأسبقية (دخول/خروج) — لمعاينة نموذج نسيان البصمة.</summary>
    [HttpGet("punches")]
    public async Task<IActionResult> DayPunches([FromQuery] string date)
    {
        if (RequireEmployee() is { } bad) return bad;
        if (!DateOnly.TryParse(date, out var d))
            return BadRequest(new { message = "تاريخ غير صالح." });

        var times = await PunchTypingEngine.DayPunchTimesAsync(_db, EmployeeId, d);
        var typed = PunchTypingEngine.Derive(times);
        return Ok(typed.Select(p => new { at = p.At.ToString("HH:mm"), type = p.Type, typeText = p.TypeLabel }));
    }

    // PunchType لم يعد يُرسله العميل — يُشتَق تلقائياً بالأسبقية الزمنية.
    public sealed record MissingPunchRequestBody(string Date, string Time, string? Reason);

    /// <summary>تقديم طلب بصمة مفقودة (يصل لصفحة إدارة الطلبات بمصدر «خدمة ذاتية»).</summary>
    [HttpPost("missing-punch")]
    public async Task<IActionResult> SubmitMissingPunch([FromBody] MissingPunchRequestBody body)
    {
        if (RequireEmployee() is { } bad) return bad;

        var eligibility = await EmployeeRequestEligibility.CheckAsync(
            _db, EmployeeId, HttpContext.RequestAborted);
        if (!eligibility.IsEligible)
            return BadRequest(new { message = eligibility.Message });

        if (!await SelfServiceAccessPolicy.IsAllowedAsync(
                _db, HttpContext, "PunchCorrection"))
            return Forbid();

        if (body is null || !DateOnly.TryParse(body.Date, out var d) || !TimeOnly.TryParse(body.Time, out var t))
            return BadRequest(new { message = "أدخل تاريخ ووقت البصمة." });

        var punchAt = d.ToDateTime(t);
        // النوع يُشتَق بالأسبقية الزمنية بين بصمات اليوم والبصمة المُضافة (لا اختيار يدوي).
        var existingTimes = await PunchTypingEngine.DayPunchTimesAsync(_db, EmployeeId, d);
        var derivedType = PunchTypingEngine.DeriveTypeFor(existingTimes, punchAt);

        var (ok, message) = await MissingPunchRequestStore.SaveAsync(
            _db, SmartAttendance.Web.Infrastructure.Security.CompanyScope.Unrestricted(), new MissingPunchRequestStore.Request
        {
            EmployeeId = EmployeeId,
            PunchAt = punchAt,
            PunchType = derivedType,
            Reason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim(),
            Source = "خدمة ذاتية"
        }, User.Identity?.Name ?? "employee", EmployeeId);

        return ok ? Ok(new { message, derivedType }) : BadRequest(new { message });
    }

    /// <summary>
    /// صندوق الموافقات الخاص بالمستخدم الحالي. لا يعيد إلا الطلبات التي يستطيع
    /// المستخدم البتّ بخطوتها الحالية وفق محرك الموافقات نفسه.
    /// </summary>
    [HttpGet("approvals")]
    public async Task<IActionResult> Approvals()
    {
        if (RequireEmployee() is { } bad) return bad;

        var scope = await ApprovalScopeAsync();
        if (scope.IsDeniedAll) return Ok(Array.Empty<object>());

        var scopeFilter = EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId");
        var candidates = await HrmsDatabase.QueryAsync(
            _db,
            $"""
SELECT TOP 100
    r.Id,
    r.EmployeeId,
    ISNULL(e.EmployeeNo, N'') AS EmployeeNo,
    ISNULL(e.FullName, N'') AS EmployeeName,
    LTRIM(RTRIM(CONCAT(
        ISNULL(e.FirstNameEn,N''), N' ',
        ISNULL(e.SecondNameEn,N''), N' ',
        ISNULL(e.ThirdNameEn,N''), N' ',
        ISNULL(e.LastNameEn,N'')))) AS EmployeeNameEn,
    ISNULL(e.Position, N'') AS Position,
    ISNULL((
        SELECT TOP 1 lev.Value FROM LocalizedEntityValues lev
        WHERE lev.CompanyId=e.CompanyId
          AND lev.EntityType=N'Position'
          AND lev.EntityId=e.PositionId
          AND lev.FieldName=N'Name'
          AND lev.CultureCode=N'en-US'
          AND ISNULL(lev.IsDeleted,0)=0
        ORDER BY lev.Id DESC
    ),N'') AS PositionEn,
    ISNULL(d.Name, N'') AS Department,
    ISNULL((
        SELECT TOP 1 lev.Value FROM LocalizedEntityValues lev
        WHERE lev.CompanyId=e.CompanyId
          AND lev.EntityType=N'Department'
          AND lev.EntityId=e.DepartmentId
          AND lev.FieldName=N'Name'
          AND lev.CultureCode=N'en-US'
          AND ISNULL(lev.IsDeleted,0)=0
        ORDER BY lev.Id DESC
    ),N'') AS DepartmentEn,
    ISNULL(b.Name, N'') AS Branch,
    ISNULL((
        SELECT TOP 1 lev.Value FROM LocalizedEntityValues lev
        WHERE lev.CompanyId=e.CompanyId
          AND lev.EntityType=N'Branch'
          AND lev.EntityId=e.BranchId
          AND lev.FieldName=N'Name'
          AND lev.CultureCode=N'en-US'
          AND ISNULL(lev.IsDeleted,0)=0
        ORDER BY lev.Id DESC
    ),N'') AS BranchEn,
    ISNULL(r.RequestType, N'') AS RequestType,
    ISNULL(rt.NameEn,N'') AS RequestTypeEn,
    r.FromDate,
    r.ToDate,
    r.StartTime,
    r.EndTime,
    r.DaysCount,
    ISNULL(r.Reason, N'') AS Reason,
    r.CreatedAt,
    ISNULL(r.Status, N'Pending') AS Status
FROM SelfServiceRequests r
INNER JOIN Employees e ON e.Id = r.EmployeeId
LEFT JOIN Departments d ON d.Id = e.DepartmentId
LEFT JOIN Branches b ON b.Id = e.BranchId
LEFT JOIN RequestTypes rt ON rt.Id = r.RequestTypeId
WHERE ISNULL(r.Status, N'Pending') = N'Pending'
  AND ISNULL(e.IsDeleted, 0) = 0
  AND {scopeFilter}
ORDER BY r.CreatedAt DESC;
""",
            null,
            reader => new
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "EmployeeName"),
                EmployeeNameEn = HrmsDatabase.GetString(reader, "EmployeeNameEn"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                PositionEn = HrmsDatabase.GetString(reader, "PositionEn"),
                Department = HrmsDatabase.GetString(reader, "Department"),
                DepartmentEn = HrmsDatabase.GetString(reader, "DepartmentEn"),
                Branch = HrmsDatabase.GetString(reader, "Branch"),
                BranchEn = HrmsDatabase.GetString(reader, "BranchEn"),
                RequestType = HrmsDatabase.GetString(reader, "RequestType"),
                RequestTypeEn = HrmsDatabase.GetString(reader, "RequestTypeEn"),
                FromDate = HrmsDatabase.GetDateTime(reader, "FromDate"),
                ToDate = HrmsDatabase.GetDateTime(reader, "ToDate"),
                StartTime = HrmsDatabase.GetTimeSpan(reader, "StartTime"),
                EndTime = HrmsDatabase.GetTimeSpan(reader, "EndTime"),
                DaysCount = HrmsDatabase.GetNullableDecimal(reader, "DaysCount"),
                Reason = HrmsDatabase.GetString(reader, "Reason"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt"),
                Status = HrmsDatabase.GetString(reader, "Status")
            });

        var ids = candidates.Select(item => item.Id).Distinct().ToArray();
        var dataChanges = await DataChangeRequestStore.ListFieldsForRequestsAsync(_db, ids);
        var financials = await FinancialRequestStore.ListForRequestsAsync(_db, ids);

        var actor = ActorName;
        var roles = ActorRoles;
        var result = new List<object>();

        foreach (var item in candidates)
        {
            // Inbox الموبايل قراءة صرفة: لا نبدأ Flow ولا نرسل إشعارات عند فتح الشاشة.
            // الطلبات القديمة بلا Flow تظل لمسار الترحيل الإداري القائم.
            var flow = await ApprovalWorkflowEngine.GetFlowAsync(_db, item.Id);
            if (flow is null ||
                !await ApprovalWorkflowEngine.CanActAsync(
                    _db,
                    item.Id,
                    actor,
                    roles,
                    EmployeeId))
            {
                continue;
            }

            dataChanges.TryGetValue(item.Id, out var fields);
            financials.TryGetValue(item.Id, out var financial);

            result.Add(new
            {
                id = item.Id,
                employeeId = item.EmployeeId,
                item.EmployeeNo,
                item.EmployeeName,
                item.EmployeeNameEn,
                item.Position,
                item.PositionEn,
                item.Department,
                item.DepartmentEn,
                item.Branch,
                item.BranchEn,
                item.RequestType,
                item.RequestTypeEn,
                fromDate = item.FromDate?.ToString("yyyy-MM-dd"),
                toDate = item.ToDate?.ToString("yyyy-MM-dd"),
                startTime = item.StartTime?.ToString(@"hh\:mm"),
                endTime = item.EndTime?.ToString(@"hh\:mm"),
                item.DaysCount,
                item.Reason,
                createdAt = item.CreatedAt?.ToString("yyyy-MM-dd HH:mm"),
                currentStep = string.Join(
                    " / ",
                    flow.CurrentSteps.Select(step => step.DisplayName)),
                commentRequiredOnReject = flow.CommentRequiredOnReject,
                dataChangeFields = (fields ?? [])
                    .Select(field => new
                    {
                        key = field.Key,
                        label = field.Label,
                        oldValue = field.OldValue,
                        newValue = field.NewValue,
                        decision = field.Decision
                    })
                    .ToArray(),
                financial = financial is null
                    ? null
                    : new
                    {
                        kind = financial.Kind,
                        kindLabel = financial.KindLabelText,
                        amount = financial.Amount,
                        installmentCount = financial.InstallmentCount,
                        period = financial.PeriodText,
                        raiseType = financial.RaiseType,
                        paymentType = financial.PaymentType,
                        taxable = financial.Taxable,
                        reason = financial.Reason,
                        note = financial.Note
                    }
            });
        }

        return Ok(result);
    }

    public sealed class MobileApprovalActionBody
    {
        public string? Note { get; set; }
        public List<string>? ApprovedFieldKeys { get; set; }
    }

    [HttpPost("approvals/{id:int}/approve")]
    public async Task<IActionResult> ApproveRequest(
        int id,
        [FromBody] MobileApprovalActionBody? body)
    {
        if (RequireEmployee() is { } bad) return bad;

        var request = (await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT TOP 1 Id, ISNULL(RequestType, N'') AS RequestType
FROM SelfServiceRequests
WHERE Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id),
            reader => new
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                RequestType = HrmsDatabase.GetString(reader, "RequestType")
            })).FirstOrDefault();

        if (request is null) return NotFound(new { message = "الطلب غير موجود." });

        if (string.Equals(
                request.RequestType,
                DataChangeRequestStore.RequestTypeLabel,
                StringComparison.OrdinalIgnoreCase) &&
            body?.ApprovedFieldKeys is null)
        {
            return BadRequest(new
            {
                message = "حدد حقول تعديل البيانات المقبولة قبل اعتماد الطلب."
            });
        }

        var scope = await ApprovalScopeAsync();
        var action = await ApprovalWorkflowEngine.ApproveAsync(
            _db,
            scope,
            id,
            ActorName,
            string.IsNullOrWhiteSpace(body?.Note) ? null : body!.Note!.Trim(),
            ActorRoles,
            EmployeeId,
            body?.ApprovedFieldKeys);

        if (!action.Ok) return BadRequest(new { message = action.Message });

        var effectsPending = false;
        if (action.FinalApproved)
        {
            try
            {
                await ApplyApprovalEffectsAsync(id, scope);
            }
            catch
            {
                // الاعتماد ومهمة الأثر حُفظا ذرياً؛ العامل الخلفي سيعيد المحاولة.
                effectsPending = true;
            }
        }

        return Ok(new
        {
            message = effectsPending
                ? action.Message + " تم حفظ الأثر للتنفيذ التلقائي عند عودة الخدمة."
                : action.Message,
            finalApproved = action.FinalApproved,
            effectsPending
        });
    }

    [HttpPost("approvals/{id:int}/reject")]
    public async Task<IActionResult> RejectRequest(
        int id,
        [FromBody] MobileApprovalActionBody? body)
    {
        if (RequireEmployee() is { } bad) return bad;

        var scope = await ApprovalScopeAsync();
        var action = await ApprovalWorkflowEngine.RejectAsync(
            _db,
            scope,
            id,
            ActorName,
            string.IsNullOrWhiteSpace(body?.Note) ? null : body!.Note!.Trim(),
            ActorRoles,
            EmployeeId);

        return action.Ok
            ? Ok(new { message = action.Message })
            : BadRequest(new { message = action.Message });
    }

    [HttpPost("approvals/{id:int}/return")]
    public async Task<IActionResult> ReturnRequestForRevision(
        int id,
        [FromBody] MobileApprovalActionBody? body)
    {
        if (RequireEmployee() is { } bad) return bad;

        var scope = await ApprovalScopeAsync();
        var action = await ApprovalWorkflowEngine.ReturnForRevisionAsync(
            _db,
            scope,
            id,
            ActorName,
            string.IsNullOrWhiteSpace(body?.Note) ? null : body!.Note!.Trim(),
            ActorRoles,
            EmployeeId);

        return action.Ok
            ? Ok(new { message = action.Message })
            : BadRequest(new { message = action.Message });
    }

    private async Task ApplyApprovalEffectsAsync(
        int id,
        CompanyScope scope)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await ApprovalEffectJobStore.ApplyNowAsync(
            _db,
            id,
            scope,
            ActorName,
            ip);
    }

    /// <summary>طلبات الخدمة الذاتية العامة الخاصة بي (إجازة/مغادرة/...).</summary>
    [HttpGet("requests")]
    public async Task<IActionResult> MyRequests()
    {
        if (RequireEmployee() is { } bad) return bad;
        var rows = await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT TOP 100
       r.Id,r.RequestTypeId,r.RequestType,
       ISNULL(t.NameEn,N'') AS RequestTypeEn,
       r.FromDate,r.ToDate,r.StartTime,r.EndTime,
       r.Reason,r.Status,r.CreatedAt,
       CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(r.AttachmentPath,N''))),N'') IS NULL
            THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS HasAttachment
FROM SelfServiceRequests r
LEFT JOIN RequestTypes t ON t.Id = r.RequestTypeId
WHERE r.EmployeeId = @Id
ORDER BY r.CreatedAt DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", EmployeeId),
            reader => new
            {
                id = HrmsDatabase.GetInt(reader,"Id"),
                requestTypeId = HrmsDatabase.GetNullableInt(reader, "RequestTypeId"),
                type = HrmsDatabase.GetString(reader, "RequestType"),
                typeEn = HrmsDatabase.GetString(reader, "RequestTypeEn"),
                fromDate = HrmsDatabase.GetDateTime(reader, "FromDate")?.ToString("yyyy-MM-dd"),
                toDate = HrmsDatabase.GetDateTime(reader, "ToDate")?.ToString("yyyy-MM-dd"),
                startTime = HrmsDatabase.GetTimeSpan(reader, "StartTime")?.ToString(@"hh\:mm"),
                endTime = HrmsDatabase.GetTimeSpan(reader, "EndTime")?.ToString(@"hh\:mm"),
                reason = HrmsDatabase.GetString(reader, "Reason"),
                status = HrmsDatabase.GetString(reader, "Status"),
                hasAttachment = HrmsDatabase.GetBool(reader, "HasAttachment"),
                createdAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")?.ToString("yyyy-MM-dd HH:mm")
            });
        return Ok(rows);
    }

    public sealed record SelfServiceRequestBody(string RequestType, string FromDate, string? ToDate, string? Reason);

    /// <summary>
    /// المسار القديم محفوظ فقط لمنع نسخ 0.3.0 من إنشاء طلبات ناقصة بلا هوية نوع/وقت/مرفق.
    /// </summary>
    [HttpPost("requests")]
    public IActionResult SubmitLegacyRequest([FromBody] SelfServiceRequestBody body) =>
        BadRequest(new
        {
            message = "هذه النسخة من تطبيق ZYNORA قديمة لإرسال الطلبات. حدّث التطبيق ثم أعد المحاولة."
        });

    public sealed class MobileRequestForm
    {
        public int RequestTypeId { get; set; }
        public string FromDate { get; set; } = string.Empty;
        public string? ToDate { get; set; }
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
        public string? Reason { get; set; }
        public IFormFile? Attachment { get; set; }
    }

    /// <summary>إنشاء طلب موبايل مُهيكل من كتالوج الشركة الفعّال.</summary>
    [HttpPost("requests/create")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> CreateRequest([FromForm] MobileRequestForm form)
    {
        if (RequireEmployee() is { } bad) return bad;

        var eligibility = await EmployeeRequestEligibility.CheckAsync(
            _db, EmployeeId, HttpContext.RequestAborted);
        if (!eligibility.IsEligible)
            return BadRequest(new { message = eligibility.Message });

        if (form is null || form.RequestTypeId <= 0)
            return BadRequest(new { message = "اختر نوع الطلب." });

        await RequestTypeStore.EnsureAsync(_db);
        var type = await RequestTypeStore.GetTypeAsync(_db, form.RequestTypeId);
        if (type is null || !type.IsActive)
            return BadRequest(new { message = "نوع الطلب غير متاح حالياً." });

        var action = ActionForRequestType(type);
        if (action is null ||
            !await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, action))
            return Forbid();

        if (!DateOnly.TryParse(form.FromDate, out var from))
            return BadRequest(new { message = "تاريخ البداية مطلوب." });

        var to = DateOnly.TryParse(form.ToDate, out var parsedTo) ? parsedTo : from;
        if (to < from)
            return BadRequest(new { message = "تاريخ النهاية قبل البداية." });

        TimeSpan? startTime = null;
        TimeSpan? endTime = null;
        if (type.NeedsTime)
        {
            if (!TimeSpan.TryParse(form.StartTime, out var parsedStart) ||
                !TimeSpan.TryParse(form.EndTime, out var parsedEnd))
                return BadRequest(new { message = "وقت البداية والنهاية مطلوبان لهذا النوع." });

            startTime = parsedStart;
            endTime = parsedEnd;
            if (to == from && parsedEnd <= parsedStart)
                to = from.AddDays(1);

            var incomplete = await FindIncompletePunchDayAsync(EmployeeId, from, to);
            if (incomplete is { } missingDate)
                return BadRequest(new
                {
                    message = $"لا يمكن تقديم طلب زمني قبل معالجة البصمة الناقصة ليوم {missingDate:yyyy-MM-dd}."
                });
        }

        var hasAttachment = form.Attachment is { Length: > 0 };

        var days = to.DayNumber - from.DayNumber + 1;
        if (type.AllowedDays is int maxDays && days > maxDays)
            return BadRequest(new { message = $"عدد الأيام يتجاوز المسموح ({maxDays} يوم)." });

        var reason = string.IsNullOrWhiteSpace(form.Reason)
            ? "تم الإرسال من تطبيق الموبايل"
            : form.Reason.Trim();

        var policy = await CompanyLeavePolicyStore.ValidateRequestAsync(
            _db, EmployeeId, 0, type.Id, type.Name,
            from, to, startTime, endTime, reason, hasAttachment);
        if (!policy.Ok)
            return BadRequest(new { message = policy.Message });

        string? attachmentPath = null;
        if (hasAttachment)
        {
            attachmentPath = await _protectedFiles.SaveAsync(
                form.Attachment, EmployeeId, "request", HttpContext.RequestAborted);
            if (attachmentPath is null)
                return BadRequest(new
                {
                    message = "تعذر قبول المرفق. استخدم ملفاً مدعوماً وبحجم مسموح ثم أعد المحاولة."
                });
        }

        var requestId = await HrmsDatabase.ScalarAsync<int>(
            _db,
            """
INSERT INTO SelfServiceRequests
(EmployeeId,RequestTypeId,RequestType,CreatedAt,FromDate,ToDate,StartTime,EndTime,
 Reason,Status,DaysCount,AttachmentPath,RequestSource)
VALUES
(@Emp,@TypeId,@Type,SYSUTCDATETIME(),@From,@To,@Start,@End,
 @Reason,'Pending',@Days,@Attachment,N'SelfService');
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Emp", EmployeeId);
                HrmsDatabase.AddParameter(command, "@TypeId", type.Id);
                HrmsDatabase.AddParameter(command, "@Type", type.Name);
                HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", to.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@Start", (object?)startTime ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@End", (object?)endTime ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Reason", reason);
                HrmsDatabase.AddParameter(command, "@Days", (decimal)days);
                HrmsDatabase.AddParameter(command, "@Attachment", (object?)attachmentPath ?? DBNull.Value);
            });

        var start = await ApprovalWorkflowEngine.StartAsync(
            _db, requestId, type.Name, EmployeeId);
        if (!start.Ok)
            return BadRequest(new { message = start.Message, requestId });

        return Ok(new
        {
            message = $"تم إرسال طلب {type.Name} وهو قيد المراجعة.",
            requestId
        });
    }

    public sealed record CancelRequestBody(string? Reason);

    /// <summary>إلغاء طلب الموظف نفسه ضمن مهلة القالب المجمدة وقت تقديمه.</summary>
    [HttpPost("requests/{requestId:int}/cancel")]
    public async Task<IActionResult> CancelRequest(int requestId,[FromBody] CancelRequestBody? body)
    {
        if(RequireEmployee() is { } bad) return bad;
        var result=await ApprovalWorkflowEngine.CancelByRequesterAsync(
            _db,requestId,EmployeeId,User.Identity?.Name ?? EmployeeId.ToString(),body?.Reason);
        return result.Ok ? Ok(new { message=result.Message }) : BadRequest(new { message=result.Message });
    }

    /// <summary>الحقول القابلة لطلب تعديلها + قيمتها الحالية وخيارات القوائم (لبناء نموذج «تعديل بياناتي»).</summary>
    [HttpGet("data-change/fields")]
    public async Task<IActionResult> DataChangeFields()
    {
        if (RequireEmployee() is { } bad) return bad;
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, "UpdateMyData"))
            return Forbid();

        var fields = await DataChangeRequestStore.ListEditableAsync(_db, EmployeeId);
        var result = new List<object>();

        foreach (var field in fields)
        {
            var options = field.Kind == "select"
                ? await DataChangeRequestStore.OptionsAsync(_db, field.OptionsKey)
                : new List<DataChangeRequestStore.Option>();

            result.Add(new
            {
                key = field.Key,
                label = field.Label,
                currentValue = field.OldValue,
                kind = field.Kind,
                options = options.Select(option => new
                {
                    value = option.Value,
                    label = option.Label
                }).ToArray()
            });
        }

        return Ok(result);
    }

    public sealed record DataChangeItem(string Key, string? NewValue);
    public sealed record DataChangeBody(List<DataChangeItem> Fields, string? Reason);

    /// <summary>تقديم طلب تعديل بيانات (لا يُطبَّق إلا بعد اعتماد لجنة الموافقة).</summary>
    [HttpPost("data-change")]
    public async Task<IActionResult> SubmitDataChange([FromBody] DataChangeBody body)
    {
        if (RequireEmployee() is { } bad) return bad;
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, "UpdateMyData"))
            return Forbid();
        if (body?.Fields is not { Count: > 0 })
            return BadRequest(new { message = "اختر حقلاً واحداً على الأقل لتعديله." });

        // القيم الحالية للتحقق من وجود تغيير فعلي.
        var editable = await DataChangeRequestStore.ListEditableAsync(_db, EmployeeId);
        var currentByKey = editable.ToDictionary(f => f.Key, f => f, StringComparer.OrdinalIgnoreCase);

        var proposed = new List<DataChangeRequestStore.ProposedField>();
        foreach (var item in body.Fields)
        {
            if (!currentByKey.TryGetValue(item.Key ?? "", out var def)) continue;
            proposed.Add(new DataChangeRequestStore.ProposedField
            {
                Key = def.Key,
                OldValue = def.OldValue,
                NewValue = item.NewValue
            });
        }
        if (proposed.Count == 0)
            return BadRequest(new { message = "لا توجد حقول صالحة للتعديل." });

        var reason = string.IsNullOrWhiteSpace(body.Reason) ? "طلب تعديل بيانات من تطبيق الموبايل" : body.Reason!.Trim();

        var requestId = await HrmsDatabase.ScalarAsync<int>(
            _db,
            """
INSERT INTO SelfServiceRequests (EmployeeId, RequestType, CreatedAt, Reason, Status, RequestSource)
VALUES (@Emp, @Type, SYSUTCDATETIME(), @Reason, 'Pending', N'SelfService');
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Emp", EmployeeId);
                HrmsDatabase.AddParameter(command, "@Type", DataChangeRequestStore.RequestTypeLabel);
                HrmsDatabase.AddParameter(command, "@Reason", reason);
            });

        if (requestId <= 0)
            return BadRequest(new { message = "تعذّر إنشاء الطلب." });

        var savedCount = await DataChangeRequestStore.SaveFieldsAsync(_db, requestId, proposed);
        if (savedCount == 0)
        {
            await HrmsDatabase.ExecuteAsync(_db, "DELETE FROM SelfServiceRequests WHERE Id=@r",
                cmd => HrmsDatabase.AddParameter(cmd, "@r", requestId));
            return BadRequest(new { message = "لم تُدخِل أي قيمة مختلفة عن الحالية." });
        }

        var start=await ApprovalWorkflowEngine.StartAsync(_db, requestId, DataChangeRequestStore.RequestTypeLabel, EmployeeId);
        if(!start.Ok) return BadRequest(new { message=start.Message, requestId });
        return Ok(new { message = $"تم إرسال طلب تعديل البيانات ({savedCount} حقل) وهو قيد المراجعة.", requestId });
    }

    /// <summary>نسخة Multipart للموبايل: تعديل حقول + صورة شخصية ضمن نفس طلب الموافقة.</summary>
    [HttpPost("data-change/multipart")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SubmitDataChangeMultipart(
        [FromForm] string? FieldsJson,
        [FromForm] string? Reason,
        [FromForm] IFormFile? Photo,
        [FromServices] IWebHostEnvironment environment)
    {
        if (RequireEmployee() is { } bad) return bad;
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, "UpdateMyData"))
            return Forbid();

        var eligibility = await EmployeeRequestEligibility.CheckAsync(
            _db, EmployeeId, HttpContext.RequestAborted);
        if (!eligibility.IsEligible)
            return BadRequest(new { message = eligibility.Message });

        List<DataChangeItem> items;
        try
        {
            items = string.IsNullOrWhiteSpace(FieldsJson)
                ? new List<DataChangeItem>()
                : System.Text.Json.JsonSerializer.Deserialize<List<DataChangeItem>>(
                    FieldsJson,
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
                  ?? new List<DataChangeItem>();
        }
        catch
        {
            return BadRequest(new { message = "بيانات التعديل غير صالحة." });
        }

        var editable = await DataChangeRequestStore.ListEditableAsync(_db, EmployeeId);
        var currentByKey = editable.ToDictionary(field => field.Key, StringComparer.OrdinalIgnoreCase);
        var proposed = new List<DataChangeRequestStore.ProposedField>();

        foreach (var item in items)
        {
            if (!currentByKey.TryGetValue(item.Key ?? "", out var def) ||
                string.Equals(def.Kind, "photo", StringComparison.OrdinalIgnoreCase))
                continue;

            var newValue = item.NewValue?.Trim();
            if (string.IsNullOrWhiteSpace(newValue))
                continue;

            if (string.Equals(def.Kind, "select", StringComparison.OrdinalIgnoreCase))
            {
                var options = await DataChangeRequestStore.OptionsAsync(_db, def.OptionsKey);
                if (!options.Any(option =>
                        string.Equals(option.Value, newValue, StringComparison.OrdinalIgnoreCase)))
                    continue;
            }

            proposed.Add(new DataChangeRequestStore.ProposedField
            {
                Key = def.Key,
                OldValue = def.OldValue,
                NewValue = newValue
            });
        }

        if (Photo is { Length: > 0 })
        {
            var extension = Path.GetExtension(Photo.FileName);
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".webp"
            };

            if (string.IsNullOrWhiteSpace(extension) || !allowed.Contains(extension))
                return BadRequest(new { message = "صيغة الصورة غير مدعومة (JPG/PNG/WEBP)." });
            if (Photo.Length > 5 * 1024 * 1024)
                return BadRequest(new { message = "حجم الصورة أكبر من 5MB." });
            if (!await UploadSignatureValidator.IsValidImageAsync(Photo))
                return BadRequest(new { message = "محتوى الملف ليس صورة صالحة." });

            var directory = Path.Combine(
                environment.WebRootPath,
                "uploads",
                "employee-photos");
            Directory.CreateDirectory(directory);

            var storedName =
                $"emp_{EmployeeId}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension.ToLowerInvariant()}";
            var physicalPath = Path.Combine(directory, storedName);
            await using (var stream = System.IO.File.Create(physicalPath))
                await Photo.CopyToAsync(stream, HttpContext.RequestAborted);

            currentByKey.TryGetValue("PhotoPath", out var photoDef);
            proposed.Add(new DataChangeRequestStore.ProposedField
            {
                Key = "PhotoPath",
                OldValue = photoDef?.OldValue,
                NewValue = $"/uploads/employee-photos/{storedName}"
            });
        }

        if (proposed.Count == 0)
            return BadRequest(new { message = "لم تُدخِل أي قيمة جديدة لتعديلها." });

        var normalizedReason = string.IsNullOrWhiteSpace(Reason)
            ? "طلب تعديل بيانات من تطبيق الموبايل"
            : Reason.Trim();

        var requestId = await HrmsDatabase.ScalarAsync<int>(
            _db,
            """
INSERT INTO SelfServiceRequests (EmployeeId, RequestType, CreatedAt, Reason, Status, RequestSource)
VALUES (@Emp, @Type, SYSUTCDATETIME(), @Reason, 'Pending', N'SelfService');
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Emp", EmployeeId);
                HrmsDatabase.AddParameter(command, "@Type", DataChangeRequestStore.RequestTypeLabel);
                HrmsDatabase.AddParameter(command, "@Reason", normalizedReason);
            });

        if (requestId <= 0)
            return BadRequest(new { message = "تعذّر إنشاء الطلب." });

        var savedCount = await DataChangeRequestStore.SaveFieldsAsync(_db, requestId, proposed);
        if (savedCount == 0)
        {
            await HrmsDatabase.ExecuteAsync(
                _db,
                "DELETE FROM SelfServiceRequests WHERE Id=@r",
                command => HrmsDatabase.AddParameter(command, "@r", requestId));
            return BadRequest(new { message = "لم تُدخِل أي قيمة مختلفة عن الحالية." });
        }

        var start = await ApprovalWorkflowEngine.StartAsync(
            _db,
            requestId,
            DataChangeRequestStore.RequestTypeLabel,
            EmployeeId);

        return start.Ok
            ? Ok(new
            {
                message = $"تم إرسال طلب تعديل البيانات ({savedCount} حقل) وهو قيد المراجعة.",
                requestId
            })
            : BadRequest(new { message = start.Message, requestId });
    }

    public sealed record FinancialRequestBody(
        string Kind,
        decimal Amount,
        int InstallmentCount,
        int StartYear,
        int StartMonth,
        string? Reason);

    /// <summary>كتالوج الطلبات المالية المتاحة للموظف نفسه.</summary>
    [HttpGet("financial/catalog")]
    public async Task<IActionResult> FinancialCatalog()
    {
        if (RequireEmployee() is { } bad) return bad;
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, "FinancialRequest"))
            return Forbid();

        var eligibility = await EmployeeRequestEligibility.CheckAsync(
            _db, EmployeeId, HttpContext.RequestAborted);

        return Ok(new
        {
            eligible = eligibility.IsEligible,
            message = eligibility.IsEligible ? (string?)null : eligibility.Message,
            items = FinancialRequestStore.Catalog
                .Where(kind => kind.Key != FinancialRequestStore.Raise)
                .Select(kind => new
                {
                    key = kind.Key,
                    label = kind.Label,
                    hint = kind.Hint
                })
                .ToArray()
        });
    }

    /// <summary>تقديم طلب مالي ذاتي (قرض/سلفة/بدل/استرداد).</summary>
    [HttpPost("financial")]
    public async Task<IActionResult> SubmitFinancial([FromBody] FinancialRequestBody body)
    {
        if (RequireEmployee() is { } bad) return bad;
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, "FinancialRequest"))
            return Forbid();

        var eligibility = await EmployeeRequestEligibility.CheckAsync(
            _db, EmployeeId, HttpContext.RequestAborted);
        if (!eligibility.IsEligible)
            return BadRequest(new { message = eligibility.Message });

        var kind = FinancialRequestStore.KindOf(body.Kind);
        if (kind is null || kind.Key == FinancialRequestStore.Raise)
            return BadRequest(new { message = "نوع الطلب المالي غير صالح." });
        if (body.Amount <= 0)
            return BadRequest(new { message = "المبلغ مطلوب ويجب أن يكون أكبر من صفر." });

        var today = DateTime.Today;
        var year = body.StartYear is >= 2000 and <= 2200 ? body.StartYear : today.Year;
        var month = body.StartMonth is >= 1 and <= 12 ? body.StartMonth : today.Month;

        var detail = new FinancialRequestStore.Detail
        {
            Kind = kind.Key,
            Amount = body.Amount,
            InstallmentCount = Math.Max(1, body.InstallmentCount),
            StartYear = year,
            StartMonth = month,
            PaymentType = kind.Key == FinancialRequestStore.Reimbursement ? "OutSalary" : "InSalary",
            Reason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim()
        };

        var requestId = await FinancialRequestStore.SubmitAsync(
            _db,
            detail,
            EmployeeId,
            User.Identity?.Name ?? EmployeeId.ToString(),
            "SelfService");

        return requestId > 0
            ? Ok(new
            {
                message = $"تم إرسال طلب {FinancialRequestStore.KindLabel(kind.Key)} وهو الآن قيد المراجعة.",
                requestId
            })
            : BadRequest(new { message = "تعذّر إرسال الطلب المالي." });
    }

    public sealed record ShiftRequestBody(
        int ShiftTypeId,
        DateOnly FromDate,
        DateOnly? ToDate,
        string? Reason);

    /// <summary>المناوبات المسموح بطلبها من الخدمة الذاتية.</summary>
    [HttpGet("shift/catalog")]
    public async Task<IActionResult> ShiftCatalog()
    {
        if (RequireEmployee() is { } bad) return bad;
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, "ShiftRequest"))
            return Forbid();

        var eligibility = await EmployeeRequestEligibility.CheckAsync(
            _db, EmployeeId, HttpContext.RequestAborted);

        var shifts = eligibility.IsEligible
            ? await ShiftRequestStore.RequestableShiftsAsync(_db)
            : new List<(int Id, string Name)>();

        return Ok(new
        {
            eligible = eligibility.IsEligible,
            message = eligibility.IsEligible ? (string?)null : eligibility.Message,
            items = shifts.Select(shift => new { id = shift.Id, name = shift.Name }).ToArray()
        });
    }

    /// <summary>تقديم طلب مناوبة ذاتي.</summary>
    [HttpPost("shift")]
    public async Task<IActionResult> SubmitShift([FromBody] ShiftRequestBody body)
    {
        if (RequireEmployee() is { } bad) return bad;
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, "ShiftRequest"))
            return Forbid();

        var eligibility = await EmployeeRequestEligibility.CheckAsync(
            _db, EmployeeId, HttpContext.RequestAborted);
        if (!eligibility.IsEligible)
            return BadRequest(new { message = eligibility.Message });
        if (body.ShiftTypeId <= 0)
            return BadRequest(new { message = "اختر المناوبة المطلوبة." });

        var to = body.ToDate ?? body.FromDate;
        var requestId = await ShiftRequestStore.SubmitAsync(
            _db,
            EmployeeId,
            body.ShiftTypeId,
            body.FromDate,
            to,
            string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim(),
            User.Identity?.Name ?? EmployeeId.ToString());

        return requestId > 0
            ? Ok(new { message = "تم إرسال طلب المناوبة وهو الآن قيد المراجعة.", requestId })
            : BadRequest(new { message = "تعذّر إرسال طلب المناوبة — تأكد أن المناوبة متاحة للطلب." });
    }

    /// <summary>الاستبيانات والنماذج الشبيهة بالاستبيان المتاحة للموظف الحالي.</summary>
    [HttpGet("surveys")]
    public async Task<IActionResult> Surveys()
    {
        if (RequireEmployee() is { } bad) return bad;

        var available = (await FormSubmissionStore.AvailableForAsync(
                _db,
                EmployeeId,
                DateOnly.FromDateTime(DateTime.Today)))
            .Where(template => template.IsSurvey)
            .ToList();

        var mine = await FormSubmissionStore.LoadAsync(_db, employeeId: EmployeeId);
        var submittedTemplateIds = mine
            .Where(item => item.IsSurvey)
            .Select(item => item.TemplateId)
            .ToHashSet();

        return Ok(available.Select(template => new
        {
            id = template.Id,
            name = template.Name,
            nameEn = template.NameEn,
            description = template.Description,
            formType = template.FormType,
            typeLabel = template.TypeLabel,
            submitted = submittedTemplateIds.Contains(template.Id)
        }));
    }

    /// <summary>تفاصيل استبيان متاح للموظف الحالي مع المجموعات والحقول.</summary>
    [HttpGet("surveys/{id:int}")]
    public async Task<IActionResult> SurveyDetail(int id)
    {
        if (RequireEmployee() is { } bad) return bad;
        if (id <= 0) return BadRequest(new { message = "معرّف الاستبيان غير صالح." });

        var available = await FormSubmissionStore.AvailableForAsync(
            _db,
            EmployeeId,
            DateOnly.FromDateTime(DateTime.Today));

        var template = available.FirstOrDefault(item => item.Id == id && item.IsSurvey);
        if (template is null)
            return NotFound(new { message = "هذا الاستبيان غير متاح لك." });

        var groups = await FormTemplateStore.LoadGroupsAsync(_db, template.Id);
        var fields = await FormTemplateStore.LoadFieldsAsync(_db, template.Id);
        var mine = await FormSubmissionStore.LoadAsync(
            _db,
            templateId: template.Id,
            employeeId: EmployeeId);

        return Ok(new
        {
            id = template.Id,
            name = template.Name,
            nameEn = template.NameEn,
            description = template.Description,
            formType = template.FormType,
            typeLabel = template.TypeLabel,
            submitted = mine.Any(item => item.IsSurvey),
            groups = groups.Select(group => new
            {
                id = group.Id,
                name = group.Name,
                nameEn = group.NameEn,
                sortOrder = group.SortOrder
            }),
            fields = fields.Select(field => new
            {
                id = field.Id,
                groupId = field.GroupId,
                label = field.Label,
                labelEn = field.LabelEn,
                controlType = field.ControlType,
                controlLabel = field.ControlLabel,
                options = field.OptionList,
                scale = field.EffectiveScale,
                required = field.IsRequired,
                sortOrder = field.SortOrder
            })
        });
    }

    public sealed class PollItemDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Question { get; set; } = "";
        public string Category { get; set; } = "";
        public DateTime? PublishDate { get; set; }
        public bool HasVoted { get; set; }
        public List<PollOptionDto> Options { get; set; } = new();
    }

    public sealed record PollOptionDto(int Id, string Text, int DisplayOrder);
    public sealed record PollVoteBody(int OptionId);

    private async Task<(string EmployeeNo, string Department, string Branch)?> PollAudienceAsync()
    {
        var rows = await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT TOP 1
       ISNULL(e.EmployeeNo, N'') AS EmployeeNo,
       ISNULL(d.Name, N'') AS DepartmentName,
       ISNULL(b.Name, N'') AS BranchName
FROM Employees e
LEFT JOIN Departments d ON d.Id = e.DepartmentId
LEFT JOIN Branches b ON b.Id = e.BranchId
WHERE e.Id = @EmployeeId
  AND ISNULL(e.IsDeleted, 0) = 0;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", EmployeeId),
            reader => (
                HrmsDatabase.GetString(reader, "EmployeeNo"),
                HrmsDatabase.GetString(reader, "DepartmentName"),
                HrmsDatabase.GetString(reader, "BranchName")));

        return rows.Count == 0 ? null : rows[0];
    }

    /// <summary>استطلاعات Pulse المنشورة والموجّهة للموظف الحالي.</summary>
    [HttpGet("polls")]
    public async Task<IActionResult> Polls()
    {
        if (RequireEmployee() is { } bad) return bad;
        await EmployeeEngagementSchema.EnsureAsync(_db);

        var audience = await PollAudienceAsync();
        if (audience is null)
            return NotFound(new { message = "الموظف غير موجود." });

        var polls = await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT TOP 10
       p.Id, p.Title, ISNULL(p.Question, N'') AS Question,
       ISNULL(p.Category, N'استطلاع') AS Category, p.PublishDate,
       CASE WHEN EXISTS
       (
           SELECT 1 FROM EmployeePollVotes v
           WHERE v.PollId = p.Id AND v.EmployeeId = @EmployeeId
       )
       THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS HasVoted
FROM EmployeePolls p
WHERE p.IsPublished = 1
  AND (p.CompanyId IS NULL
       OR p.CompanyId = (SELECT e.CompanyId FROM Employees e WHERE e.Id = @EmployeeId))
  AND
  (
      p.TargetType IS NULL
      OR p.TargetType = 'All'
      OR (p.TargetType = 'Employee'
          AND (p.TargetValue = @EmployeeIdText
               OR p.TargetValue = @EmployeeNo
               OR p.TargetValue LIKE @EmployeeIdLike))
      OR (p.TargetType = 'Department' AND p.TargetValue = @DepartmentName)
      OR (p.TargetType = 'Branch' AND p.TargetValue = @BranchName)
  )
ORDER BY p.PublishDate DESC, p.Id DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", EmployeeId);
                HrmsDatabase.AddParameter(command, "@EmployeeIdText", EmployeeId.ToString());
                HrmsDatabase.AddParameter(command, "@EmployeeNo", audience.Value.EmployeeNo);
                HrmsDatabase.AddParameter(command, "@EmployeeIdLike", $"%{EmployeeId}%");
                HrmsDatabase.AddParameter(command, "@DepartmentName", audience.Value.Department);
                HrmsDatabase.AddParameter(command, "@BranchName", audience.Value.Branch);
            },
            reader => new PollItemDto
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Title = HrmsDatabase.GetString(reader, "Title"),
                Question = HrmsDatabase.GetString(reader, "Question"),
                Category = HrmsDatabase.GetString(reader, "Category"),
                PublishDate = HrmsDatabase.GetDateTime(reader, "PublishDate"),
                HasVoted = HrmsDatabase.GetBool(reader, "HasVoted")
            });

        foreach (var poll in polls)
        {
            poll.Options = await HrmsDatabase.QueryAsync(
                _db,
                """
SELECT Id, OptionText, DisplayOrder
FROM EmployeePollOptions
WHERE PollId = @PollId
ORDER BY DisplayOrder, Id;
""",
                command => HrmsDatabase.AddParameter(command, "@PollId", poll.Id),
                reader => new PollOptionDto(
                    HrmsDatabase.GetInt(reader, "Id"),
                    HrmsDatabase.GetString(reader, "OptionText"),
                    HrmsDatabase.GetInt(reader, "DisplayOrder")));
        }

        return Ok(polls);
    }

    /// <summary>تسجيل تصويت واحد في استطلاع Pulse متاح للموظف الحالي.</summary>
    [HttpPost("polls/{id:int}/vote")]
    public async Task<IActionResult> VotePoll(int id, [FromBody] PollVoteBody body)
    {
        if (RequireEmployee() is { } bad) return bad;
        if (id <= 0 || body.OptionId <= 0)
            return BadRequest(new { message = "اختر إجابة قبل الإرسال." });

        await EmployeeEngagementSchema.EnsureAsync(_db);

        var audience = await PollAudienceAsync();
        if (audience is null)
            return NotFound(new { message = "الموظف غير موجود." });

        var votable = await HrmsDatabase.ScalarAsync<int>(
            _db,
            """
SELECT COUNT(1)
FROM EmployeePolls p
INNER JOIN EmployeePollOptions o
        ON o.PollId = p.Id
       AND o.Id = @OptionId
WHERE p.Id = @PollId
  AND p.IsPublished = 1
  AND (p.CompanyId IS NULL
       OR p.CompanyId = (SELECT e.CompanyId FROM Employees e WHERE e.Id = @EmployeeId))
  AND
  (
      p.TargetType IS NULL
      OR p.TargetType = 'All'
      OR (p.TargetType = 'Employee'
          AND (p.TargetValue = @EmployeeIdText
               OR p.TargetValue = @EmployeeNo
               OR p.TargetValue LIKE @EmployeeIdLike))
      OR (p.TargetType = 'Department' AND p.TargetValue = @DepartmentName)
      OR (p.TargetType = 'Branch' AND p.TargetValue = @BranchName)
  );
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@PollId", id);
                HrmsDatabase.AddParameter(command, "@OptionId", body.OptionId);
                HrmsDatabase.AddParameter(command, "@EmployeeId", EmployeeId);
                HrmsDatabase.AddParameter(command, "@EmployeeIdText", EmployeeId.ToString());
                HrmsDatabase.AddParameter(command, "@EmployeeNo", audience.Value.EmployeeNo);
                HrmsDatabase.AddParameter(command, "@EmployeeIdLike", $"%{EmployeeId}%");
                HrmsDatabase.AddParameter(command, "@DepartmentName", audience.Value.Department);
                HrmsDatabase.AddParameter(command, "@BranchName", audience.Value.Branch);
            });

        if (votable == 0)
            return BadRequest(new { message = "الاستطلاع غير متاح." });

        var existing = await HrmsDatabase.ScalarAsync<int>(
            _db,
            """
SELECT COUNT(1)
FROM EmployeePollVotes
WHERE PollId = @PollId
  AND EmployeeId = @EmployeeId;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@PollId", id);
                HrmsDatabase.AddParameter(command, "@EmployeeId", EmployeeId);
            });

        if (existing > 0)
            return BadRequest(new { message = "تم تسجيل تصويتك مسبقاً لهذا الاستطلاع." });

        await HrmsDatabase.ExecuteAsync(
            _db,
            """
INSERT INTO EmployeePollVotes (PollId, OptionId, EmployeeId, VotedAt)
VALUES (@PollId, @OptionId, @EmployeeId, SYSUTCDATETIME());
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@PollId", id);
                HrmsDatabase.AddParameter(command, "@OptionId", body.OptionId);
                HrmsDatabase.AddParameter(command, "@EmployeeId", EmployeeId);
            });

        return Ok(new { message = "تم تسجيل تصويتك بنجاح. شكراً لمشاركتك." });
    }

    public sealed record SurveyAnswerBody(int FieldId, string? Value);
    public sealed record SurveySubmitBody(
        Guid SubmissionToken,
        List<SurveyAnswerBody>? Answers);

    /// <summary>حفظ إجابات استبيان متاح للموظف الحالي بنفس قواعد البوابة.</summary>
    [HttpPost("surveys/{id:int}")]
    public async Task<IActionResult> SubmitSurvey(
        int id,
        [FromBody] SurveySubmitBody body)
    {
        if (RequireEmployee() is { } bad) return bad;
        if (id <= 0) return BadRequest(new { message = "معرّف الاستبيان غير صالح." });

        var available = await FormSubmissionStore.AvailableForAsync(
            _db,
            EmployeeId,
            DateOnly.FromDateTime(DateTime.Today));

        var template = available.FirstOrDefault(item => item.Id == id && item.IsSurvey);
        if (template is null)
            return NotFound(new { message = "هذا الاستبيان غير متاح لك." });

        var fields = await FormTemplateStore.LoadFieldsAsync(_db, template.Id);
        var answerMap = (body.Answers ?? new List<SurveyAnswerBody>())
            .Where(answer => fields.Any(field => field.Id == answer.FieldId))
            .GroupBy(answer => answer.FieldId)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value);

        var validationRows = fields
            .Select(field => (
                field.Label,
                (string?)field.ControlType,
                field.IsRequired,
                field.Options,
                answerMap.TryGetValue(field.Id, out var value) ? value : null))
            .ToList();

        var errors = FormBuilder.ValidateSubmission(validationRows);
        if (errors.Count > 0)
            return BadRequest(new
            {
                message = string.Join(Environment.NewLine, errors),
                errors
            });

        var answers = fields
            .Select(field => (
                FieldId: field.Id,
                Label: field.Label,
                ControlType: field.ControlType,
                Value: answerMap.TryGetValue(field.Id, out var value) ? value : null,
                SortOrder: field.SortOrder))
            .ToList();

        var result = await FormSubmissionStore.SubmitAsync(
            _db,
            template,
            EmployeeId,
            answers,
            User.Identity?.Name,
            body.SubmissionToken == Guid.Empty
                ? Guid.NewGuid()
                : body.SubmissionToken);

        return Ok(new
        {
            message = result.IsDuplicate
                ? "سبق تسجيل هذه الإجابة."
                : "شكراً — سُجِّلت إجاباتك.",
            submissionId = result.SubmissionId,
            duplicate = result.IsDuplicate
        });
    }

    public sealed record FeedbackBody(
        string? Type,
        string? Priority,
        string? Title,
        string? Message);

    /// <summary>آخر الشكاوى والمقترحات الخاصة بالموظف الحالي.</summary>
    [HttpGet("feedback")]
    public async Task<IActionResult> Feedback()
    {
        if (RequireEmployee() is { } bad) return bad;

        await EmployeeEngagementSchema.EnsureAsync(_db);

        var items = await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT TOP 20
       Id, Type, Title, ISNULL(Message, N'') AS Message,
       ISNULL(Priority, N'') AS Priority, Status,
       ISNULL(AdminReply, N'') AS AdminReply,
       ISNULL(RepliedBy, N'') AS RepliedBy,
       RepliedAt, CreatedAt
FROM EmployeeFeedbackItems
WHERE EmployeeId = @EmployeeId
ORDER BY CreatedAt DESC, Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", EmployeeId),
            reader => new
            {
                id = HrmsDatabase.GetInt(reader, "Id"),
                type = HrmsDatabase.GetString(reader, "Type"),
                title = HrmsDatabase.GetString(reader, "Title"),
                message = HrmsDatabase.GetString(reader, "Message"),
                priority = HrmsDatabase.GetString(reader, "Priority"),
                status = HrmsDatabase.GetString(reader, "Status"),
                adminReply = HrmsDatabase.GetString(reader, "AdminReply"),
                repliedBy = HrmsDatabase.GetString(reader, "RepliedBy"),
                repliedAt = HrmsDatabase.GetDateTime(reader, "RepliedAt"),
                createdAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });

        return Ok(items);
    }

    /// <summary>إرسال شكوى أو مقترح أو استفسار من تطبيق الموظف.</summary>
    [HttpPost("feedback")]
    public async Task<IActionResult> SubmitFeedback([FromBody] FeedbackBody body)
    {
        if (RequireEmployee() is { } bad) return bad;

        await EmployeeEngagementSchema.EnsureAsync(_db);

        var eligibility = await EmployeeRequestEligibility.CheckAsync(
            _db,
            EmployeeId,
            HttpContext.RequestAborted);
        if (!eligibility.IsEligible)
            return BadRequest(new { message = eligibility.Message });

        var type = body.Type?.Trim() ?? string.Empty;
        var priority = body.Priority?.Trim() ?? string.Empty;
        var title = body.Title?.Trim() ?? string.Empty;
        var message = body.Message?.Trim() ?? string.Empty;

        var allowedTypes = new[] { "اقتراح", "شكوى", "استفسار" };
        var allowedPriorities = new[] { "منخفض", "متوسط", "عالي" };

        if (!allowedTypes.Contains(type, StringComparer.Ordinal))
            return BadRequest(new { message = "نوع الرسالة غير صالح." });
        if (!allowedPriorities.Contains(priority, StringComparer.Ordinal))
            return BadRequest(new { message = "الأولوية غير صالحة." });
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
            return BadRequest(new { message = "العنوان والتفاصيل مطلوبان." });

        await HrmsDatabase.ExecuteAsync(
            _db,
            """
INSERT INTO EmployeeFeedbackItems
    (EmployeeId, Type, Title, Message, Priority, Status, CreatedAt)
VALUES
    (@EmployeeId, @Type, @Title, @Message, @Priority, N'Open', SYSUTCDATETIME());
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", EmployeeId);
                HrmsDatabase.AddParameter(command, "@Type", type);
                HrmsDatabase.AddParameter(command, "@Title", title);
                HrmsDatabase.AddParameter(command, "@Message", message);
                HrmsDatabase.AddParameter(command, "@Priority", priority);
            });

        return Ok(new { message = "تم إرسال رسالتك للموارد البشرية وهي الآن قيد المتابعة." });
    }

    /// <summary>سجل الانضباط والمخالفات للموظف الحالي.</summary>
    [HttpGet("discipline")]
    public async Task<IActionResult> Discipline()
    {
        if (RequireEmployee() is { } bad) return bad;

        await ViolationCaseSchema.EnsureAsync(_db);

        var items = await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT ReferenceNo,
       EventDate,
       ISNULL(ViolationCategory, N'') AS ViolationCategory,
       ISNULL(ViolationTitle, N'') AS ViolationTitle,
       ISNULL(ActionStatus, N'') AS ActionStatus,
       ISNULL(Status, N'') AS Status,
       ISNULL(FinalPenaltyAction, N'') AS FinalPenaltyAction,
       ISNULL(DeductionAmount, 0) AS DeductionAmount,
       ISNULL(EmployeeReplyStatus, N'NotRequested') AS EmployeeReplyStatus,
       ISNULL(EmployeeReply, N'') AS EmployeeReply
FROM EmployeeViolationCases
WHERE EmployeeId = @EmployeeId
  AND ISNULL(IsDeleted, 0) = 0
ORDER BY EventDate DESC, Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", EmployeeId),
            reader => new
            {
                referenceNo = HrmsDatabase.GetString(reader, "ReferenceNo"),
                eventDate = HrmsDatabase.GetDateOnly(reader, "EventDate")?.ToString("yyyy-MM-dd"),
                category = HrmsDatabase.GetString(reader, "ViolationCategory"),
                title = HrmsDatabase.GetString(reader, "ViolationTitle"),
                actionStatus = HrmsDatabase.GetString(reader, "ActionStatus"),
                status = HrmsDatabase.GetString(reader, "Status"),
                finalPenaltyAction = HrmsDatabase.GetString(reader, "FinalPenaltyAction"),
                deductionAmount = reader.IsDBNull(reader.GetOrdinal("DeductionAmount"))
                    ? 0m
                    : Convert.ToDecimal(reader["DeductionAmount"]),
                replyStatus = HrmsDatabase.GetString(reader, "EmployeeReplyStatus"),
                employeeReply = HrmsDatabase.GetString(reader, "EmployeeReply")
            });

        return Ok(items);
    }

    private static string? ActionForRequestType(RequestTypeStore.ReqType type)
    {
        var effect = type.EffectCode?.Trim() ?? string.Empty;
        if (effect.Contains("Overtime", StringComparison.OrdinalIgnoreCase))
            return "OvertimeRequest";
        if (effect.Contains("ExitPermission", StringComparison.OrdinalIgnoreCase))
            return "ExitPermission";
        if (effect.Contains("MissingPunch", StringComparison.OrdinalIgnoreCase))
            return "PunchCorrection";
        if (effect.StartsWith("Leave", StringComparison.OrdinalIgnoreCase) ||
            effect.Equals("BusinessTrip", StringComparison.OrdinalIgnoreCase))
            return "LeaveRequest";

        return SelfServiceAccessPolicy.ActionForRequestType(type.Name);
    }

    private async Task<DateOnly?> FindIncompletePunchDayAsync(
        int employeeId, DateOnly from, DateOnly to)
    {
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var times = await PunchTypingEngine.DayPunchTimesAsync(_db, employeeId, day);
            if (times.Count % 2 != 0)
                return day;
        }

        return null;
    }
}
