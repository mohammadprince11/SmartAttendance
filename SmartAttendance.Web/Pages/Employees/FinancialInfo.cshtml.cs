using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Employees;

/// <summary>
/// Edit an employee's financial / payroll setup (Kayan "المعلومات المالية").
/// One row per employee (upsert). Access follows the same People edit permission
/// as the profile Edit page.
/// </summary>
public class FinancialInfoModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;
    private readonly IPermissionAuthorizationService _permissionAuthorizationService;

    private readonly Infrastructure.Security.IProtectedFileService _protectedFiles;

    public FinancialInfoModel(
        ApplicationDbContext dbContext,
        IWebHostEnvironment environment,
        IPermissionAuthorizationService permissionAuthorizationService,
        Infrastructure.Security.IProtectedFileService protectedFiles)
    {
        _dbContext = dbContext;
        _environment = environment;
        _permissionAuthorizationService = permissionAuthorizationService;
        _protectedFiles = protectedFiles;
    }

    /// <summary>رابط فتح مرفق مالي (تعهّد/مرفق) بنقطة مصادَقة موقّعة.</summary>
    public string FileUrl(string? storedPath) => _protectedFiles.BuildUrl(EmployeeId, storedPath);

    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeNo { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }

    /// <summary>مجموع العلاوات النشطة اليوم — لعرض «الراتب الكلي = أساسي + علاوات» حياً.</summary>
    public decimal ActiveAllowancesTotal { get; set; }

    [BindProperty] public EmployeeFinancialInfo Input { get; set; } = new();
    [BindProperty] public IFormFile? CommitmentFile { get; set; }
    [BindProperty] public IFormFile? Attachment { get; set; }

    /// <summary>ملفات الضريبة/الضمان المتاحة للإسناد — تُعبّئ المنسدلتين.</summary>
    public List<PayrollConfigStore.TaxProfile> TaxProfiles { get; set; } = new();
    public List<PayrollConfigStore.GosiProfile> GosiProfiles { get; set; } = new();

    /// <summary>الملف الذي سيُطبَّق فعلاً على هذا الموظف، ولماذا — «لماذا هذا الملف؟» يُجاب بالشاشة لا بالتخمين.</summary>
    public PayrollProfileResolver.Resolution TaxChoice { get; set; } = PayrollProfileResolver.NoProfile;
    public PayrollProfileResolver.Resolution GosiChoice { get; set; } = PayrollProfileResolver.NoProfile;

    /// <summary>وعاء الضمان المحسوب (أساسي + علاوات نشطة بحسب عضوية وعاء الملف الفائز) وحصّتاه.</summary>
    public decimal GosiBase { get; set; }
    public EmployeeDefinedSalaryBase.Source GosiBaseSource { get; set; }
    public string GosiBaseSourceLabel => GosiBaseSource switch
    {
        EmployeeDefinedSalaryBase.Source.Employee => "مصدر الوعاء: راتب الضمان المُدخَل",
        EmployeeDefinedSalaryBase.Source.EmployeeMissing => "⚠ اختير «مُعرَّف بالموظف» بلا قيمة — احتُسب من المكوّنات",
        _ => "مصدر الوعاء: مُركَّب من المكوّنات"
    };
    public decimal GosiEmployeeShare { get; set; }
    public decimal GosiCompanyShare { get; set; }

    public static readonly string[] Currencies = { "IQD", "USD", "EUR", "SAR", "AED", "JOD", "EGP", "KWD", "BHD", "QAR", "OMR" };
    public static readonly string[] PaymentMethods = { "نقداً", "شيك", "تحويل بنكي" };

    /// <summary>
    /// الحقول الحساسة: الصفحة كاملة راتب — تُحجب عن غير المخوّلين. الرؤية = قائمة
    /// الأدوار القديمة (Sensitive.SalaryRoles، توافقية) **أو** منح People.ViewCompensation
    /// صريحاً بأدوار الوصول ضمن نطاق هذا الموظف.
    /// </summary>
    private async Task<bool> CanViewSalaryAsync(int employeeId)
    {
        var allowedRoles = await SmartAttendance.Web.Infrastructure.HrSettings.HrSettingsStore.GetAsync(
            _dbContext, "Sensitive.SalaryRoles", "Admin,HR Manager");
        var role = PeopleAccessContext.GetRole(HttpContext);
        var roleAllowed = allowedRoles
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(role, StringComparer.OrdinalIgnoreCase);

        if (roleAllowed) return true;

        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        return await _permissionAuthorizationService.CanAccessEmployeeAsync(
                   systemUserId, PeoplePermissionCodes.ViewCompensation, employeeId,
                   PeopleCompatibilityAccess.IsAllowed(role, PeoplePermissionCodes.ViewCompensation),
                   HttpContext.RequestAborted) ||
               await AccessRoleStore.HasSensitiveFieldAsync(
                   _dbContext, systemUserId, SensitiveFieldCatalog.Salary);
    }

    /// <summary>
    /// كتابة الراتب/الإعداد المالي تتطلّب People.EditCompensation لا مجرّد الرؤية —
    /// نفس مسار الرؤية لكن برمز التعديل، والتوافقية تمنعه لغير (Admin · HR Manager).
    /// </summary>
    private async Task<bool> CanEditCompensationAsync(int employeeId)
    {
        var role = PeopleAccessContext.GetRole(HttpContext);
        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        return await _permissionAuthorizationService.CanAccessEmployeeAsync(
            systemUserId, PeoplePermissionCodes.EditCompensation, employeeId,
            PeopleCompatibilityAccess.IsAllowed(role, PeoplePermissionCodes.EditCompensation),
            HttpContext.RequestAborted);
    }

    /// <summary>درجات سلم الرواتب النشطة (نظير كيان) — قائمة اقتراح للحقل النصي، لا قيد عليه.</summary>
    public List<SalaryScaleStore.Grade> ScaleGrades { get; set; } = new();

    /// <summary>الدرجة المقترحة من الأساسي الحالي (أول درجة يقع الأساسي بمداها) — عرض فقط.</summary>
    public SalaryScaleStore.Grade? SuggestedGrade { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await CanViewSalaryAsync(id)) return Forbid();
        if (!await CanEditAsync(id)) return Forbid();
        await EmployeeFinancialInfoSchema.EnsureAsync(_dbContext);
        var scopeProvider = HttpContext.RequestServices.GetService(typeof(ICompanyScopeProvider)) as ICompanyScopeProvider;
        var scope = scopeProvider is null ? CompanyScope.DeniedAll() : await scopeProvider.GetAsync(HttpContext.RequestAborted);

        try
        {
            // درجات السلم المرئية لنطاق شركات المستخدم (المشتركة + شركاته) — عزل تهيئة 8D–8M.
            ScaleGrades = await SalaryScaleStore.ListAsync(_dbContext, scope, activeOnly: true);
        }
        catch
        {
            // الجدول يأتي بهجرة 20260816-02 — قاعدة لم تُهاجَر بعد لا تكسر الملف المالي.
            ScaleGrades = new();
        }

        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);
        if (employee == null) return NotFound();
        if (!scope.Allows(employee.CompanyId)) return Forbid();

        EmployeeId = employee.Id;
        EmployeeName = employee.FullName;
        EmployeeNo = employee.EmployeeNo;

        Input = await _dbContext.EmployeeFinancialInfos.AsNoTracking()
            .FirstOrDefaultAsync(f => f.EmployeeId == id)
            ?? new EmployeeFinancialInfo { EmployeeId = id };

        await EmployeeAllowanceSchema.EnsureAsync(_dbContext);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var allowances = await _dbContext.EmployeeAllowances.AsNoTracking()
            .Where(a => a.EmployeeId == id)
            .ToListAsync();
        ActiveAllowancesTotal = allowances.Where(a => a.IsActiveOn(today)).Sum(a => a.Amount);

        await LoadProfilesAsync(id, today, scope, employee.CompanyId);

        // اقتراح درجة السلم من الأساسي — عرضٌ لا فرض.
        if (Input.BasicSalary is > 0)
            SuggestedGrade = SalaryScaleStore.Suggest(ScaleGrades, Input.BasicSalary.Value);

        return Page();
    }

    /// <summary>
    /// يقرأ الملفات ويحسم الفائز لهذا الموظف ثم يحسب وعاء الضمان **للعرض فقط**.
    /// الرقم يُعاد حسابه بالمسير بنفس المركِّب (<see cref="SalaryBaseComposer"/>)
    /// فلا نسخة ثانية من الصيغة هنا؛ وما يُعرض تقديرٌ باليوم لا التزامٌ بقسيمة.
    /// </summary>
    private async Task LoadProfilesAsync(int employeeId, DateOnly today, CompanyScope scope, int? companyId)
    {
        TaxProfiles = await PayrollConfigStore.ListTaxProfilesAsync(_dbContext, scope, companyId);
        GosiProfiles = await PayrollConfigStore.ListGosiProfilesAsync(_dbContext, scope, companyId);

        var rows = await HrConditionFacts.LoadAsync(_dbContext, employeeId);
        var facts = rows.Count > 0
            ? HrConditionFacts.Build(rows[0], today)
            : new Dictionary<string, HrConditions.Fact>();

        TaxChoice = PayrollProfileResolver.Resolve(
            Input.TaxProfileId, PayrollConfigStore.Candidates(TaxProfiles), facts);
        GosiChoice = PayrollProfileResolver.Resolve(
            Input.GosiProfileId, PayrollConfigStore.Candidates(GosiProfiles), facts);

        var winner = GosiChoice.ProfileId is { } id
            ? GosiProfiles.FirstOrDefault(profile => profile.Id == id)
            : null;

        var members = await SalaryBaseStore.MembersAsync(
            _dbContext, SalaryBaseComposer.GosiBaseKey, winner?.Id ?? 0);

        var basic = Input.BasicSalary ?? 0;
        var composedGosi = SalaryBaseComposer.Compose(
            new SalaryBaseComposer.Amounts
            {
                Basic = basic,
                Allowances = ActiveAllowancesTotal,
                Gross = basic + ActiveAllowancesTotal
            },
            members);

        // Issue 16: المعاينة تحسم الوعاء بنفس منطق المسير (EmployeeDefinedSalaryBase)
        // فلا تعرض المُركَّب حين يكون النمط «مُعرَّف بالموظف» — وإلا اختلف رقم الشاشة
        // عن القسيمة (2,000,000 بالمعاينة و400,000 بالاحتساب).
        var gosiResolved = EmployeeDefinedSalaryBase.Resolve(
            Input.GosiBaseMode, Input.SocialSecuritySalary, composedGosi);
        GosiBase = gosiResolved.Base;
        GosiBaseSource = gosiResolved.Source;

        (GosiEmployeeShare, GosiCompanyShare) = PayrollConfigStore.ComputeGosi(GosiBase, winner);
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await CanEditCompensationAsync(id)) return Forbid();
        if (!await CanEditAsync(id)) return Forbid();
        await EmployeeFinancialInfoSchema.EnsureAsync(_dbContext);

        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);
        if (employee == null) return NotFound();
        var scopeProvider = HttpContext.RequestServices.GetService(typeof(ICompanyScopeProvider)) as ICompanyScopeProvider;
        var scope = scopeProvider is null ? CompanyScope.DeniedAll() : await scopeProvider.GetAsync(HttpContext.RequestAborted);
        if (!scope.Allows(employee.CompanyId)) return Forbid();

        EmployeeId = employee.Id;
        EmployeeName = employee.FullName;
        EmployeeNo = employee.EmployeeNo;

        // حرّاس Phase 24: الرواتب المُدخَلة غير سالبة — تُرفض لا تُنتج راتباً خاطئاً.
        var salaryErrors = PayrollConfigValidation.ValidateEmployeeSalaries(
            Input.BasicSalary, Input.CurrentTaxSalary, Input.SocialSecuritySalary);
        if (salaryErrors.Count > 0)
        {
            ErrorMessage = string.Join(" · ", salaryErrors);
            await LoadProfilesAsync(id, DateOnly.FromDateTime(DateTime.Today), scope, employee.CompanyId);
            return Page();
        }

        var allowedTaxProfiles = await PayrollConfigStore.ListTaxProfilesAsync(_dbContext, scope, employee.CompanyId);
        var allowedGosiProfiles = await PayrollConfigStore.ListGosiProfilesAsync(_dbContext, scope, employee.CompanyId);
        if (Input.TaxProfileId is > 0 && allowedTaxProfiles.All(profile => profile.Id != Input.TaxProfileId) ||
            Input.GosiProfileId is > 0 && allowedGosiProfiles.All(profile => profile.Id != Input.GosiProfileId))
            return Forbid();

        var user = User.Identity?.Name ?? "System";
        var now = DateTime.UtcNow;

        var entity = await _dbContext.EmployeeFinancialInfos.FirstOrDefaultAsync(f => f.EmployeeId == id);
        if (entity == null)
        {
            entity = new EmployeeFinancialInfo { EmployeeId = id, CreatedAt = now, CreatedBy = user };
            _dbContext.EmployeeFinancialInfos.Add(entity);
        }
        else
        {
            entity.UpdatedAt = now;
            entity.UpdatedBy = user;
        }

        // Salary & currency
        entity.Currency = Clean(Input.Currency);
        entity.SalaryScale = Clean(Input.SalaryScale);
        entity.BasicSalary = Input.BasicSalary;
        entity.DailySalary = Input.DailySalary;
        entity.HourlyRate = Input.HourlyRate;
        // إسناد الملفات المالية — 0/سالب يعني «بلا إسناد» (خيار «حسب الشرط/النشط»).
        entity.TaxProfileId = Input.TaxProfileId is > 0 ? Input.TaxProfileId : null;
        entity.GosiProfileId = Input.GosiProfileId is > 0 ? Input.GosiProfileId : null;
        // Social security
        entity.SocialSecurityType = Clean(Input.SocialSecurityType);
        entity.SocialSecuritySalary = Input.SocialSecuritySalary;
        // نمط وعاء الضمان — «مُعرَّف بالموظف» يجعل المسير يحسب على الراتب أعلاه لا الإجمالي.
        entity.GosiBaseMode = EmployeeDefinedSalaryBase.NormalizeMode(Input.GosiBaseMode);
        entity.SocialSecurityNo = Clean(Input.SocialSecurityNo);
        entity.SocialSecurityJoinDate = Input.SocialSecurityJoinDate;
        entity.SocialSecurityPreviousMonths = Input.SocialSecurityPreviousMonths;
        entity.RetirementAge = Input.RetirementAge;
        // Tax
        entity.TaxFile = Clean(Input.TaxFile);
        entity.TaxNo = Clean(Input.TaxNo);
        entity.TaxYear = Input.TaxYear;
        // راتب الضريبة الراهن ونمطه — «مُعرَّف بالموظف» يُحتسب عليه لا على المكوّنات.
        entity.CurrentTaxSalary = Input.CurrentTaxSalary;
        entity.TaxBaseMode = EmployeeDefinedSalaryBase.NormalizeMode(Input.TaxBaseMode);
        entity.PreviousTaxSalary = Input.PreviousTaxSalary;
        entity.PreviousTaxExemption = Input.PreviousTaxExemption;
        entity.PreviousTaxAmount = Input.PreviousTaxAmount;
        entity.PreviousMinSalary = Input.PreviousMinSalary;
        entity.PreviousMinTaxAmount = Input.PreviousMinTaxAmount;
        entity.PreviousTaxMonths = Input.PreviousTaxMonths;
        // End of service
        entity.EndOfServiceSetup = Clean(Input.EndOfServiceSetup);
        entity.EndOfServiceCompute = Input.EndOfServiceCompute;
        entity.EndOfServiceStartDate = Input.EndOfServiceStartDate;
        entity.EndOfServiceDueDate = Input.EndOfServiceDueDate;
        // Calc control
        entity.CalcPreviousSalaries = Input.CalcPreviousSalaries;
        entity.StopSalaryCalc = Input.StopSalaryCalc;
        entity.AdditionalSalaryStartDate = Input.AdditionalSalaryStartDate;
        // Payment & bank
        entity.PaymentMethod = Clean(Input.PaymentMethod);
        entity.BankName = Clean(Input.BankName);
        entity.BankBranch = Clean(Input.BankBranch);
        entity.UnitNo = Clean(Input.UnitNo);
        entity.Iban = Clean(Input.Iban);
        entity.CardNo = Clean(Input.CardNo);
        entity.MxpAccount = Clean(Input.MxpAccount);
        entity.BankCommitment = Input.BankCommitment;

        var commitment = await SaveFileAsync(CommitmentFile, id);
        if (commitment.path != null) { entity.BankCommitmentFileName = commitment.name; entity.BankCommitmentFilePath = commitment.path; }
        var attach = await SaveFileAsync(Attachment, id);
        if (attach.path != null) { entity.AttachmentName = attach.name; entity.AttachmentPath = attach.path; }

        await _dbContext.SaveChangesAsync();
        TempData["SuccessMessage"] = "تم حفظ المعلومات المالية.";
        return RedirectToPage("./Profile", new { id });
    }

    private Task<bool> CanEditAsync(int employeeId)
    {
        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var role = PeopleAccessContext.GetRole(HttpContext);
        return _permissionAuthorizationService.CanAccessEmployeeAsync(
            systemUserId,
            PeoplePermissionCodes.Edit,
            employeeId,
            PeopleCompatibilityAccess.IsAllowed(role, PeoplePermissionCodes.Edit),
            HttpContext.RequestAborted);
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static readonly string[] AllowedExtensions = { ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".doc", ".docx", ".xls", ".xlsx" };

    // المرحلة 6: المرفقات المالية (تعهّدات/حسابات) خارج wwwroot بمفتاح مولَّد،
    // والقراءة عبر نقطة مصادَقة تفحص صلاحية الموظف المستهدف.
    private async Task<(string? name, string? path)> SaveFileAsync(IFormFile? file, int employeeId)
    {
        if (file == null || file.Length == 0) return (null, null);

        var stored = await _protectedFiles.SaveAsync(
            file, employeeId, "financial", HttpContext.RequestAborted);

        return stored is null
            ? (null, null)
            : (Path.GetFileName(file.FileName), stored);
    }
}
