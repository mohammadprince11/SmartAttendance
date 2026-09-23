using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Application.Employees.Services;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;
using SmartAttendance.Web.Infrastructure.Localization;

namespace SmartAttendance.Web.Pages.Employees;

public class EditModel : PageModel
{
    private readonly IEmployeeService _employeeService;
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;
    private readonly IPermissionAuthorizationService _permissionAuthorizationService;
    private readonly ICompanyDataLocalizationService _dataLocalization;

    private static readonly HashSet<string> AllowedEmployeePhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    // تُدار ترجمات أسماء الموظف لاحقاً من شاشة البيانات متعددة اللغات، لذلك لا
    // تظهر ولا تُفرض ضمن نموذج التعديل العام، مع إبقاء القيم المخزنة بلا تغيير.
    private static readonly string[] DeferredTranslatedNameKeys =
    {
        nameof(EmployeeEditViewModel.FirstNameEn),
        nameof(EmployeeEditViewModel.SecondNameEn),
        nameof(EmployeeEditViewModel.ThirdNameEn),
        nameof(EmployeeEditViewModel.LastNameEn)
    };

    private readonly Infrastructure.Security.IProtectedFileService _protectedFiles;

    public EditModel(
        IEmployeeService employeeService,
        ApplicationDbContext dbContext,
        IWebHostEnvironment environment,
        IPermissionAuthorizationService permissionAuthorizationService,
        Infrastructure.Security.IProtectedFileService protectedFiles,
        ICompanyDataLocalizationService dataLocalization)
    {
        _employeeService = employeeService;
        _dbContext = dbContext;
        _environment = environment;
        _permissionAuthorizationService = permissionAuthorizationService;
        _protectedFiles = protectedFiles;
        _dataLocalization = dataLocalization;
    }

    [BindProperty]
    public EmployeeEditViewModel Employee { get; set; } = new();

    [BindProperty]
    public decimal? BasicSalary { get; set; }

    public bool CanEditCompensation { get; set; }

    public bool EmployeeNoLockedByPayroll { get; set; }

    public string CurrentCompanyName { get; set; } = string.Empty;

    [BindProperty]
    public List<EmployeeNameTranslationInput> EmployeeNameTranslations { get; set; } = [];

    [BindProperty]
    public IFormFile? EmployeePhoto { get; set; }

    /// <summary>صورة توقيع الموظف — تغذّي رمز الوثائق ولا تُخزَّن بمسار عام.</summary>
    [BindProperty]
    public IFormFile? EmployeeSignature { get; set; }

    /// <summary>رابط التوقيع الحالي بنقطة مصادَقة، أو فارغ إن لم يُرفع.</summary>
    public string CurrentSignatureUrl { get; set; } = string.Empty;

    [BindProperty]
    public int? DirectManagerId { get; set; }

    [BindProperty]
    [System.ComponentModel.DataAnnotations.StringLength(150)]
    public string? FamilyNumber { get; set; }

    [BindProperty]
    public long FamilyIdentityDocumentId { get; set; }

    private sealed record FamilyIdentityValue(
        long Id,
        string FamilyNumber);

    public class ManagerOption { 
        public int Id { get; set; } 
        public string EmployeeNo { get; set; } = string.Empty; 
        public string FullName { get; set; } = string.Empty; 
    }
    public IEnumerable<ManagerOption> Managers { get; set; } = new List<ManagerOption>();

    public string CurrentPhotoPath { get; set; } = string.Empty;
    public IEnumerable<BranchListViewModel> Branches { get; set; } = new List<BranchListViewModel>();
    public IEnumerable<DepartmentListViewModel> Departments { get; set; } = new List<DepartmentListViewModel>();
    public IEnumerable<PositionOptionViewModel> PositionOptions { get; set; } = new List<PositionOptionViewModel>();
    public List<EmployeeProfileDynamicSection> ProfileDynamicSections { get; set; } = new();
    public string? ErrorMessage { get; set; }

    public List<string> ReligionOptions { get; set; } = new();
    public List<string> WorkTypeOptions { get; set; } = new();
    public List<string> GradeOptions { get; set; } = new();
    public List<string> SponsorOptions { get; set; } = new();

    /// <summary>الحقول الإلزامية من «استوديو الحقول» — تعلَّم بنجمة وتُفرض بالسيرفر.</summary>
    public HashSet<string> RequiredFieldKeys { get; set; } = new();

    /// <summary>إعدادات الحقول الكاملة (إخفاء/تسمية/ترتيب) — تطبّقها الواجهة.</summary>
    public Dictionary<string, EmployeeFieldControl.FieldSetting> FieldSettings { get; set; } = new();


    private async Task LocalizeBusinessLookupsAsync()
    {
        await EmployeeBusinessDataDisplayLocalizer.LocalizeBranchesAsync(
            _dbContext,
            Branches,
            HttpContext.RequestAborted);

        await EmployeeBusinessDataDisplayLocalizer.LocalizeDepartmentsAsync(
            _dbContext,
            Departments,
            HttpContext.RequestAborted);

        await EmployeeBusinessDataDisplayLocalizer.LocalizePositionsAsync(
            _dbContext,
            PositionOptions,
            HttpContext.RequestAborted);
    }
    private async Task<bool> HasPayrollHistoryAsync(int employeeId)
    {
        if (employeeId <= 0)
        {
            return false;
        }

        var exists = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            """
            IF OBJECT_ID(N'dbo.PayrollRunLines', N'U') IS NULL
            BEGIN
                SELECT 0;
                RETURN;
            END;

            SELECT CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM dbo.PayrollRunLines
                    WHERE EmployeeId = @EmployeeId
                )
                    THEN 1
                ELSE 0
            END;
            """,
            command => HrmsDatabase.AddParameter(
                command,
                "@EmployeeId",
                employeeId));

        return exists == 1;
    }

    private async Task LoadLookupsAsync()
    {
        ReligionOptions = await HrLookups.ValuesAsync(_dbContext, "religions");
        WorkTypeOptions = await HrLookups.ValuesAsync(_dbContext, "worktypes");
        GradeOptions = await HrLookups.ValuesAsync(_dbContext, "grades");
        SponsorOptions = await HrLookups.ValuesAsync(_dbContext, "sponsors");
    }

    private async Task LoadFamilyNumberAsync(int employeeId)
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1
       Id,
       FamilyNumber
FROM dbo.EmployeeIdentityDocuments
WHERE EmployeeId = @EmployeeId
  AND DocumentType = N'NationalId'
  AND IsCurrent = 1
ORDER BY
    CASE
        WHEN NULLIF(LTRIM(RTRIM(FamilyNumber)), '') IS NOT NULL
            THEN 0
        ELSE 1
    END,
    CASE
        WHEN SourceOnboardingDocumentId IS NOT NULL
            THEN 0
        ELSE 1
    END,
    Id DESC;
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@EmployeeId",
                employeeId),
            reader => new FamilyIdentityValue(
                HrmsDatabase.GetLong(reader, "Id"),
                HrmsDatabase.GetString(reader, "FamilyNumber")));

        var identity = rows.FirstOrDefault();
        FamilyIdentityDocumentId = identity?.Id ?? 0;
        FamilyNumber = string.IsNullOrWhiteSpace(
                identity?.FamilyNumber)
            ? null
            : identity.FamilyNumber.Trim();
    }

    private async Task<bool> SaveFamilyNumberAsync(int employeeId)
    {
        var saved = await PeopleIdentityBootstrap.SetFamilyNumberAsync(
            _dbContext,
            employeeId,
            FamilyNumber,
            FamilyIdentityDocumentId);

        if (saved)
        {
            await LoadFamilyNumberAsync(employeeId);
        }

        return saved;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        // Defense-in-depth: نفس فحص صلاحية التعديل الموجود بصفحة Profile،
        // حتى لا يكون الرابط المباشر للصفحة كافياً لتجاوز الصلاحيات.
        if (!await CanEditEmployeeAsync(id))
        {
            return Forbid();
        }

        Branches = await _employeeService.GetBranchesForDropdownAsync();
        Departments = await _employeeService.GetDepartmentsForDropdownAsync();

        var employee = await _employeeService.GetEditByIdAsync(id);

        if (employee == null) return NotFound();

        Employee = employee;
        EmployeeNoLockedByPayroll = await HasPayrollHistoryAsync(Employee.Id);
        CanEditCompensation = await CanEditCompensationAsync(Employee.Id);
        if (CanEditCompensation)
        {
            BasicSalary = await LoadBasicSalaryAsync(Employee.Id);
        }

        CurrentCompanyName = await ResolveEmployeeCompanyNameAsync(Employee.BranchId);
        PrefillQuadNameFromFullName();
        await LoadFamilyNumberAsync(Employee.Id);
        var companyId = await ResolveEmployeeCompanyIdAsync(Employee.BranchId);
        await LoadEmployeeNameTranslationsAsync(companyId, false);

        PositionOptions = await _employeeService.GetPositionsForDropdownAsync();
        await LocalizeBusinessLookupsAsync();
        CurrentPhotoPath = await GetEmployeePhotoPathAsync(Employee.Id);
        CurrentSignatureUrl = await GetSignatureUrlAsync(Employee.Id);
        ProfileDynamicSections = await EmployeeProfileDynamicFields.LoadSectionsAsync(_dbContext, Employee.Id);
        Managers = await LoadManagersAsync(Employee.Id);
        DirectManagerId = Employee.DirectManagerId;
        await LoadLookupsAsync();
        FieldSettings = await EmployeeFieldControl.GetSettingsAsync(_dbContext);
        RequiredFieldKeys = EmployeeFieldControl.RequiredKeys(FieldSettings);
        RequiredFieldKeys.ExceptWith(DeferredTranslatedNameKeys);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await CanEditEmployeeAsync(Employee.Id))
        {
            return Forbid();
        }

        CanEditCompensation = await CanEditCompensationAsync(Employee.Id);
        EmployeeNoLockedByPayroll = await HasPayrollHistoryAsync(Employee.Id);
        CurrentCompanyName = await ResolveEmployeeCompanyNameAsync(Employee.BranchId);

        if (EmployeeNoLockedByPayroll)
        {
            var storedEmployeeNo = await _dbContext.Employees
                .AsNoTracking()
                .Where(employee => employee.Id == Employee.Id)
                .Select(employee => employee.EmployeeNo)
                .SingleOrDefaultAsync();

            if (storedEmployeeNo is null)
            {
                return NotFound();
            }

            if (!string.Equals(
                    storedEmployeeNo.Trim(),
                    Employee.EmployeeNo?.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                Employee.EmployeeNo = storedEmployeeNo;
                ModelState.Remove("Employee.EmployeeNo");
                ModelState.AddModelError(
                    "Employee.EmployeeNo",
                    "لا يمكن تغيير كود الموظف بعد دخوله في دورة راتب.");
            }
        }

        Branches = await _employeeService.GetBranchesForDropdownAsync();
        Departments = await _employeeService.GetDepartmentsForDropdownAsync();
        PositionOptions = await _employeeService.GetPositionsForDropdownAsync();
        await LocalizeBusinessLookupsAsync();
        CurrentPhotoPath = Employee.Id > 0 ? await GetEmployeePhotoPathAsync(Employee.Id) : string.Empty;
        ProfileDynamicSections = await EmployeeProfileDynamicFields.LoadSectionsAsync(_dbContext, Employee.Id > 0 ? Employee.Id : 0);

        Managers = await LoadManagersAsync(Employee.Id);
        await LoadLookupsAsync();
        FieldSettings = await EmployeeFieldControl.GetSettingsAsync(_dbContext);
        RequiredFieldKeys = EmployeeFieldControl.RequiredKeys(FieldSettings);
        RequiredFieldKeys.ExceptWith(DeferredTranslatedNameKeys);

        var companyId = await ResolveEmployeeCompanyIdAsync(Employee.BranchId);
        ModelState.Remove("Employee.FullName");
        ModelState.Remove("Employee.FirstName");
        ModelState.Remove("Employee.SecondName");
        ModelState.Remove("Employee.ThirdName");
        ModelState.Remove("Employee.LastName");
        await LoadEmployeeNameTranslationsAsync(companyId, true);
        await ValidateAndMapEmployeeNamesAsync(companyId);

        // حالة التوظيف مشتقة من Check «فعال» ولا تُدخل كنص مستقل.
        Employee.EmploymentStatus = Employee.IsActive ? "Active" : "Inactive";
        ModelState.Remove("Employee.EmploymentStatus");

        // التحكم بالحقول: فرض الإلزامية المركزية بالسيرفر.
        EmployeeFieldControl.ValidateRequired(Employee, RequiredFieldKeys, ModelState, "Employee");

        if (RequiredFieldKeys.Contains("FamilyNumber") &&
            string.IsNullOrWhiteSpace(FamilyNumber))
        {
            ModelState.AddModelError(
                nameof(FamilyNumber),
                "حقل «الرقم العائلي» مطلوب.");
        }

        if (BasicSalary is < 0)
        {
            ModelState.AddModelError(
                nameof(BasicSalary),
                "الراتب الأساسي لا يمكن أن يكون سالباً.");
        }

        if (BasicSalary.HasValue && !CanEditCompensation)
        {
            ModelState.AddModelError(
                nameof(BasicSalary),
                "لا تملك صلاحية إدخال أو تعديل الراتب الأساسي.");
        }

        if (!ModelState.IsValid) return Page();

        if (DirectManagerId.HasValue && DirectManagerId.Value == Employee.Id)
        {
            ErrorMessage = "لا يمكن تعيين الموظف مديراً مباشراً لنفسه.";
            return Page();
        }

        Employee.DirectManagerId = DirectManagerId;

        // P0-7 — الارتباط الوظيفي (موقع العمل · القسم · المنصب · المدير المباشر) بيانٌ
        // تنظيميّ حسّاس: نقلُ موظفٍ بين الجهات يتطلّب صلاحية People.ChangeAssignment لا
        // مجرّد People.Edit. الفحص هنا (سيرفر) لا بالواجهة: نقارن القيم المُرسَلة بالقيم
        // المخزَّنة، وإن تغيّر أيّ حقل إسناد ولم يملك المستخدم الصلاحية نرفض قبل الحفظ.
        var current = await _dbContext.Employees
            .AsNoTracking()
            .Where(x => x.Id == Employee.Id)
            .Select(x => new
            {
                x.BranchId,
                x.DepartmentId,
                x.PositionId,
                x.DirectManagerId,
                x.FirstNameEn,
                x.SecondNameEn,
                x.ThirdNameEn,
                x.LastNameEn
            })
            .FirstOrDefaultAsync();

        if (current is not null)
        {
            // الحقول الإنجليزية لم تعد جزءاً من هذا النموذج. نحفظ قيمها الحالية
            // صراحةً كي لا يحوّل غيابها من الطلب إلى NULL عند تحديث باقي البيانات.
            Employee.FirstNameEn = current.FirstNameEn;
            Employee.SecondNameEn = current.SecondNameEn;
            Employee.ThirdNameEn = current.ThirdNameEn;
            Employee.LastNameEn = current.LastNameEn;

            var english = EmployeeNameTranslations.FirstOrDefault(item =>
                item.CultureCode.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            if (english is not null)
            {
                Employee.FirstNameEn = english.FirstName;
                Employee.SecondNameEn = english.SecondName;
                Employee.ThirdNameEn = english.ThirdName;
                Employee.LastNameEn = english.LastName;
            }

            var assignmentChanged =
                current.BranchId != Employee.BranchId ||
                current.DepartmentId != Employee.DepartmentId ||
                current.PositionId != Employee.PositionId ||
                current.DirectManagerId != Employee.DirectManagerId;

            if (assignmentChanged && !await CanChangeAssignmentAsync(Employee.Id))
            {
                ErrorMessage = "تغيير الارتباط الوظيفي (موقع العمل أو القسم أو المنصب أو المدير المباشر) يتطلّب صلاحية «تغيير الارتباط الوظيفي».";
                return Page();
            }
        }

        // اشتقاق المواطنة من الجنسية إن كانت القاعدة مفعّلة (تهيئة الأشخاص) —
        // نفس قاعدة صفحة الإنشاء كي لا يفترق السلوكان.
        if (!string.IsNullOrWhiteSpace(Employee.Nationality) &&
            bool.TryParse(await SmartAttendance.Web.Infrastructure.HrSettings.HrSettingsStore.GetAsync(
                _dbContext, CitizenshipPolicy.KeyEnabled, "False"), out var citizenshipRule) &&
            citizenshipRule)
        {
            var citizenList = await SmartAttendance.Web.Infrastructure.HrSettings.HrSettingsStore.GetAsync(
                _dbContext, CitizenshipPolicy.KeyNationalities, CitizenshipPolicy.DefaultNationalities);
            Employee.IsCitizen = CitizenshipPolicy.IsCitizen(Employee.Nationality, citizenList);
        }

        // بيانات الموظف والمدير المباشر والحقول الديناميكية تُحفظ ضمن معاملة واحدة؛
        // حفظ الصورة (عملية ملفات) يبقى خارجها حتى لا يؤثر فشله على البيانات.
        await using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            var updated = await _employeeService.UpdateAsync(Employee);

            if (!updated)
            {
                ErrorMessage = "تعذر حفظ التعديل. تأكد من كود الموظف وموقع العمل والقسم والمدير المباشر.";
                return Page();
            }

            if (!await SaveFamilyNumberAsync(Employee.Id))
            {
                await transaction.RollbackAsync();
                ErrorMessage =
                    "تعذر حفظ الرقم العائلي لعدم وجود سجل بطاقة وطنية صالح لهذا الموظف.";
                return Page();
            }

            await EmployeeProfileDynamicFields.SaveAsync(_dbContext, Employee.Id, Request.Form);
            await SaveBasicSalaryAsync(Employee.Id);

            await _dataLocalization.SaveValuesAsync(
                companyId,
                "Employee",
                Employee.Id,
                EmployeeNameTranslations.SelectMany(item => new[]
                {
                    new LocalizedFieldValue(item.CultureCode, "FirstName", item.FirstName),
                    new LocalizedFieldValue(item.CultureCode, "SecondName", item.SecondName),
                    new LocalizedFieldValue(item.CultureCode, "ThirdName", item.ThirdName),
                    new LocalizedFieldValue(item.CultureCode, "LastName", item.LastName),
                    new LocalizedFieldValue(item.CultureCode, "FullName", ComposeName(item))
                }).ToList(),
                HttpContext.RequestAborted);

            await transaction.CommitAsync();
        }

        var photoResult = await SaveEmployeePhotoAsync(Employee.Id);
        var signatureResult = await SaveEmployeeSignatureAsync(Employee.Id);

        var notes = new[] { photoResult, signatureResult }
            .Where(note => !string.IsNullOrWhiteSpace(note))
            .ToList();

        TempData["SuccessMessage"] = notes.Count == 0
            ? "تم تحديث بيانات الموظف بنجاح."
            : "تم تحديث بيانات الموظف بنجاح. " + string.Join(" ", notes);

        return RedirectToPage("./Profile", new { id = Employee.Id });
    }

    // SP20_EDIT_COMPENSATION_PARITY
    private async Task<bool> CanEditCompensationAsync(int employeeId)
    {
        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var role = PeopleAccessContext.GetRole(HttpContext);

        return await _permissionAuthorizationService.CanAccessEmployeeAsync(
            systemUserId,
            PeoplePermissionCodes.EditCompensation,
            employeeId,
            PeopleCompatibilityAccess.IsAllowed(
                role,
                PeoplePermissionCodes.EditCompensation),
            HttpContext.RequestAborted);
    }

    private async Task<decimal?> LoadBasicSalaryAsync(int employeeId)
    {
        await EmployeeFinancialInfoSchema.EnsureAsync(_dbContext);

        return await HrmsDatabase.ScalarAsync<decimal?>(
            _dbContext,
            """
SELECT TOP 1 BasicSalary
FROM dbo.EmployeeFinancialInfos
WHERE EmployeeId = @EmployeeId
  AND ISNULL(IsDeleted, 0) = 0
ORDER BY Id DESC;
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@EmployeeId",
                employeeId));
    }

    private async Task SaveBasicSalaryAsync(int employeeId)
    {
        if (!BasicSalary.HasValue ||
            !CanEditCompensation ||
            employeeId <= 0)
        {
            return;
        }

        await EmployeeFinancialInfoSchema.EnsureAsync(_dbContext);

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
UPDATE dbo.EmployeeFinancialInfos
SET BasicSalary = @BasicSalary,
    UpdatedAt = SYSUTCDATETIME()
WHERE EmployeeId = @EmployeeId
  AND ISNULL(IsDeleted, 0) = 0;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO dbo.EmployeeFinancialInfos
        (EmployeeId, BasicSalary, CreatedAt, IsDeleted)
    VALUES
        (@EmployeeId, @BasicSalary, SYSUTCDATETIME(), 0);
END;
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@EmployeeId",
                    employeeId);
                HrmsDatabase.AddParameter(
                    command,
                    "@BasicSalary",
                    BasicSalary.Value);
            });
    }

    private async Task<string> ResolveEmployeeCompanyNameAsync(int branchId)
    {
        return await _dbContext.Branches
            .AsNoTracking()
            .Where(item => item.Id == branchId && !item.IsDeleted)
            .Select(item => item.Company.Name)
            .FirstOrDefaultAsync(HttpContext.RequestAborted)
            ?? string.Empty;
    }
    private async Task<int> ResolveEmployeeCompanyIdAsync(int branchId) =>
        await _dbContext.Branches.AsNoTracking()
            .Where(item => item.Id == branchId && !item.IsDeleted)
            .Select(item => item.CompanyId)
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

    private async Task LoadEmployeeNameTranslationsAsync(int companyId, bool preservePostedValues)
    {
        if (companyId <= 0) return;
        var posted = preservePostedValues
            ? EmployeeNameTranslations.ToDictionary(item => item.CultureCode, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, EmployeeNameTranslationInput>(StringComparer.OrdinalIgnoreCase);
        var languages = await _dataLocalization.GetLanguagesAsync(companyId, HttpContext.RequestAborted);
        var stored = await _dbContext.LocalizedEntityValues.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.EntityType == "Employee" &&
                item.EntityId == Employee.Id && !item.IsDeleted)
            .ToListAsync(HttpContext.RequestAborted);

        string? Stored(string culture, string field) => stored.FirstOrDefault(item =>
            item.CultureCode == culture && item.FieldName == field)?.Value;

        EmployeeNameTranslations = languages.Select(language =>
        {
            posted.TryGetValue(language.CultureCode, out var submitted);
            return new EmployeeNameTranslationInput
            {
                CompanyId = companyId,
                CultureCode = language.CultureCode,
                NativeName = language.NativeName,
                Direction = language.Direction,
                IsDefault = language.IsDefault,
                IsRequired = language.IsRequired,
                FirstName = submitted?.FirstName
                    ?? Stored(language.CultureCode, "FirstName")
                    ?? (language.IsDefault
                        ? Employee.FirstName
                        : language.CultureCode.StartsWith("en", StringComparison.OrdinalIgnoreCase)
                            ? Employee.FirstNameEn
                            : null),
                SecondName = submitted?.SecondName
                    ?? Stored(language.CultureCode, "SecondName")
                    ?? (language.IsDefault
                        ? Employee.SecondName
                        : language.CultureCode.StartsWith("en", StringComparison.OrdinalIgnoreCase)
                            ? Employee.SecondNameEn
                            : null),
                ThirdName = submitted?.ThirdName
                    ?? Stored(language.CultureCode, "ThirdName")
                    ?? (language.IsDefault
                        ? Employee.ThirdName
                        : language.CultureCode.StartsWith("en", StringComparison.OrdinalIgnoreCase)
                            ? Employee.ThirdNameEn
                            : null),
                LastName = submitted?.LastName
                    ?? Stored(language.CultureCode, "LastName")
                    ?? (language.IsDefault
                        ? Employee.LastName
                        : language.CultureCode.StartsWith("en", StringComparison.OrdinalIgnoreCase)
                            ? Employee.LastNameEn
                            : null)
            };
        }).ToList();
    }

    private async Task ValidateAndMapEmployeeNamesAsync(int companyId)
    {
        var values = EmployeeNameTranslations.SelectMany(item => new[]
        {
            new LocalizedFieldValue(item.CultureCode, "FirstName", item.FirstName),
            new LocalizedFieldValue(item.CultureCode, "LastName", item.LastName)
        }).ToList();

        var errors = await _dataLocalization.ValidateRequiredValuesAsync(
            companyId,
            new[] { "FirstName", "LastName" },
            values,
            HttpContext.RequestAborted);

        foreach (var error in errors)
        {
            ModelState.AddModelError(nameof(EmployeeNameTranslations), error);
        }

        var languages = await _dataLocalization.GetLanguagesAsync(
            companyId,
            HttpContext.RequestAborted);

        var primaryCulture = languages
            .FirstOrDefault(language => language.IsDefault)
            ?.CultureCode
            ?? languages.FirstOrDefault()?.CultureCode;

        if (string.IsNullOrWhiteSpace(primaryCulture))
        {
            ModelState.AddModelError(
                nameof(EmployeeNameTranslations),
                "تعذر تحديد اللغة الأساسية لاسم الموظف.");
            return;
        }

        var primary = EmployeeNameTranslations.FirstOrDefault(item =>
            string.Equals(
                item.CultureCode,
                primaryCulture,
                StringComparison.OrdinalIgnoreCase));

        if (primary is null)
        {
            ModelState.AddModelError(
                nameof(EmployeeNameTranslations),
                "بيانات اللغة الأساسية لاسم الموظف غير متوفرة.");
            return;
        }

        Employee.FirstName = primary.FirstName?.Trim();
        Employee.SecondName = primary.SecondName?.Trim();
        Employee.ThirdName = primary.ThirdName?.Trim();
        Employee.LastName = primary.LastName?.Trim();
        Employee.FullName = ComposeName(primary);
    }

    private static string ComposeName(EmployeeNameTranslationInput item) =>
        string.Join(" ", new[] { item.FirstName, item.SecondName, item.ThirdName, item.LastName }
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));

    // الموظفون القدامى عندهم FullName فقط؛ نوزّعه على خانات الرباعي حتى يبقى
    // مصدر إدخال الاسم واحداً بالنموذج، وإعادة تركيبه عند الحفظ تعطي نفس النص.
    private void PrefillQuadNameFromFullName()
    {
        var hasQuad = !string.IsNullOrWhiteSpace(Employee.FirstName) ||
                      !string.IsNullOrWhiteSpace(Employee.SecondName) ||
                      !string.IsNullOrWhiteSpace(Employee.ThirdName) ||
                      !string.IsNullOrWhiteSpace(Employee.LastName);

        if (hasQuad || string.IsNullOrWhiteSpace(Employee.FullName))
        {
            return;
        }

        var parts = Employee.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Employee.FirstName = parts.Length > 0 ? parts[0] : null;
        Employee.LastName = parts.Length > 1 ? parts[^1] : null;
        Employee.SecondName = parts.Length > 2 ? parts[1] : null;
        Employee.ThirdName = parts.Length > 3 ? string.Join(' ', parts[2..^1]) : null;
    }

    private Task<bool> CanEditEmployeeAsync(int employeeId)
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

    // P0-7 — بوّابة تغيير الارتباط الوظيفي: نفس مسار CanAccessEmployeeAsync لكن برمز
    // ChangeAssignment، والتوافقية تمنعه لغير (Admin · HR Manager) — فالنقل التنظيمي
    // يتطلّب تخصيصاً صريحاً بأدوار الوصول (P0-3).
    private Task<bool> CanChangeAssignmentAsync(int employeeId)
    {
        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var role = PeopleAccessContext.GetRole(HttpContext);

        return _permissionAuthorizationService.CanAccessEmployeeAsync(
            systemUserId,
            PeoplePermissionCodes.ChangeAssignment,
            employeeId,
            PeopleCompatibilityAccess.IsAllowed(role, PeoplePermissionCodes.ChangeAssignment),
            HttpContext.RequestAborted);
    }

    private async Task<IEnumerable<ManagerOption>> LoadManagersAsync(int employeeId)
    {
        // القائمة محصورة بموظفي نفس شركة الموظف الحالي، مع استثنائه من الخيارات.
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
            SELECT e.Id, ISNULL(e.EmployeeNo, '') AS EmployeeNo, e.FullName
            FROM Employees e
            INNER JOIN Branches b ON e.BranchId = b.Id
            WHERE e.IsActive = 1
              AND e.IsDeleted = 0
              AND e.Id <> @SelfId
              AND b.CompanyId = (
                  SELECT b2.CompanyId
                  FROM Employees e2
                  INNER JOIN Branches b2 ON e2.BranchId = b2.Id
                  WHERE e2.Id = @SelfId
              )
            ORDER BY e.FullName;
            """,
            cmd => HrmsDatabase.AddParameter(cmd, "@SelfId", employeeId),
            reader => new ManagerOption { Id = Convert.ToInt32(reader["Id"]), EmployeeNo = reader["EmployeeNo"].ToString() ?? "", FullName = reader["FullName"].ToString() ?? "" });
    }

    private async Task<string> GetEmployeePhotoPathAsync(int employeeId)
    {
        return await _dbContext.Employees
            .AsNoTracking()
            .Where(x => x.Id == employeeId)
            .Select(x => x.PhotoPath ?? string.Empty)
            .FirstOrDefaultAsync() ?? string.Empty;
    }

    /// <summary>
    /// يحفظ صورة التوقيع بالمخزن المحميّ ويخزّن مفتاحها. نفس تحقّق الصورة المستعمل
    /// للصورة الشخصية (امتداد + حجم + **بصمة محتوى**) لأن ملفاً بامتداد صورة ومحتوى
    /// تنفيذيّ هو الثغرة المعروفة برفع الملفات.
    /// </summary>
    private async Task<string> GetSignatureUrlAsync(int employeeId)
    {
        var stored = await _dbContext.Employees.AsNoTracking()
            .Where(x => x.Id == employeeId)
            .Select(x => x.SignaturePath)
            .FirstOrDefaultAsync();

        return string.IsNullOrWhiteSpace(stored) ? string.Empty : _protectedFiles.BuildUrl(employeeId, stored);
    }

    private async Task<string> SaveEmployeeSignatureAsync(int employeeId)
    {
        if (EmployeeSignature == null || EmployeeSignature.Length == 0) return string.Empty;

        var extension = Path.GetExtension(EmployeeSignature.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedEmployeePhotoExtensions.Contains(extension))
            return "صيغة التوقيع غير مدعومة.";
        if (EmployeeSignature.Length > 2 * 1024 * 1024) return "حجم التوقيع أكبر من 2MB.";
        if (!await UploadSignatureValidator.IsValidImageAsync(EmployeeSignature)) return "محتوى الملف ليس صورة صالحة.";

        var stored = await _protectedFiles.SaveAsync(
            EmployeeSignature, employeeId, "signature", HttpContext.RequestAborted);

        if (stored is null) return "تعذّر حفظ التوقيع.";

        await _dbContext.Employees
            .Where(x => x.Id == employeeId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.SignaturePath, stored));

        CurrentSignatureUrl = _protectedFiles.BuildUrl(employeeId, stored);
        return "تم حفظ التوقيع.";
    }

    private async Task<string> SaveEmployeePhotoAsync(int employeeId)
    {
        if (EmployeePhoto == null || EmployeePhoto.Length == 0) return string.Empty;
        var extension = Path.GetExtension(EmployeePhoto.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedEmployeePhotoExtensions.Contains(extension)) return "صيغة الصورة غير مدعومة.";
        if (EmployeePhoto.Length > 5 * 1024 * 1024) return "حجم الصورة أكبر من 5MB.";
        if (!await UploadSignatureValidator.IsValidImageAsync(EmployeePhoto)) return "محتوى الملف ليس صورة صالحة.";

        var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "employee-photos");
        Directory.CreateDirectory(uploadRoot);
        var storedName = $"employee_{employeeId}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
        var physicalPath = Path.Combine(uploadRoot, storedName);
        var relativePath = $"/uploads/employee-photos/{storedName}";

        await using (var stream = System.IO.File.Create(physicalPath)) { await EmployeePhoto.CopyToAsync(stream); }
        await _dbContext.Employees
            .Where(x => x.Id == employeeId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.PhotoPath, relativePath));
        CurrentPhotoPath = relativePath;
        return "تم حفظ الصورة.";
    }
}
