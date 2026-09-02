using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Application.Employees.Services;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.CompanyContext;
using SmartAttendance.Web.Infrastructure.HrSettings;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Localization;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Employees;

public class CreateModel : PageModel
{
    private readonly IEmployeeService _employeeService;
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;
    private readonly ICompanyDataLocalizationService _dataLocalization;

    private static readonly HashSet<string> AllowedDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx", ".xls", ".xlsx"
    };


    private static readonly HashSet<string> AllowedEmployeePhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    public CreateModel(
        IEmployeeService employeeService,
        ApplicationDbContext dbContext,
        IWebHostEnvironment environment,
        ICompanyDataLocalizationService dataLocalization)
    {
        _employeeService = employeeService;
        _dbContext = dbContext;
        _environment = environment;
        _dataLocalization = dataLocalization;
    }

    [BindProperty]
    public EmployeeCreateViewModel Employee { get; set; } = new();


    [BindProperty]
    public IFormFile? EmployeePhoto { get; set; }

    [BindProperty]
    public List<string> InitialDocumentTypes { get; set; } = new();

    [BindProperty]
    public List<string> InitialDocumentRequired { get; set; } = new();

    [BindProperty]
    public List<IFormFile> InitialDocumentFiles { get; set; } = new();

    [BindProperty]
    public List<EmployeeNameTranslationInput> EmployeeNameTranslations { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public int? SelectedCompanyId { get; set; }

    /// <summary>إنشاء حساب دخول «موظف» للموظف الجديد في نفس الخطوة.</summary>
    [BindProperty]
    public bool CreateLoginAccount { get; set; }

    [BindProperty]
    public string? LoginUsername { get; set; }

    [BindProperty]
    public string? LoginPassword { get; set; }

    /// <summary>إجبار الموظف على تغيير كلمة المرور عند أول دخول (افتراضي: نعم).</summary>
    [BindProperty]
    public bool LoginForceChange { get; set; } = true;

    public IEnumerable<BranchListViewModel> Branches { get; set; } = new List<BranchListViewModel>();

    public IReadOnlyList<EmployeeCompanyChoice> CompanyOptions { get; set; } = [];

    public IEnumerable<DepartmentListViewModel> Departments { get; set; } = new List<DepartmentListViewModel>();

    public List<EmployeeProfileDynamicSection> ProfileDynamicSections { get; set; } = new();

    public IEnumerable<PositionOptionViewModel> PositionOptions { get; set; } = new List<PositionOptionViewModel>();

    public string? ErrorMessage { get; set; }

    /// <summary>مخطط رمز الموظف مفعّل — الحقل يُترك فارغاً ليتولّد تلقائياً.</summary>
    public bool CodeSchemaActive { get; set; }
    public string? CodeSchemaPreview { get; set; }

    public List<string> ReligionOptions { get; set; } = new();
    public List<string> WorkTypeOptions { get; set; } = new();
    public List<string> GradeOptions { get; set; } = new();
    public List<string> SponsorOptions { get; set; } = new();

    /// <summary>الحقول الإلزامية من «استوديو الحقول» — تعلَّم بنجمة وتُفرض بالسيرفر.</summary>
    public HashSet<string> RequiredFieldKeys { get; set; } = new();

    public HashSet<int> CompaniesMissingLanguageSetup { get; set; } = [];

    /// <summary>إعدادات الحقول الكاملة (إخفاء/تسمية/ترتيب) — تطبّقها الواجهة.</summary>
    public Dictionary<string, EmployeeFieldControl.FieldSetting> FieldSettings { get; set; } = new();

    private async Task LoadLookupsAsync()
    {
        ReligionOptions = await HrLookups.ValuesAsync(_dbContext, "religions");
        WorkTypeOptions = await HrLookups.ValuesAsync(_dbContext, "worktypes");
        GradeOptions = await HrLookups.ValuesAsync(_dbContext, "grades");
        SponsorOptions = await HrLookups.ValuesAsync(_dbContext, "sponsors");
    }

    public async Task OnGetAsync()
    {
        Branches = await _employeeService.GetBranchesForDropdownAsync();
        await ResolveSelectedCompanyAsync();
        Departments = await _employeeService.GetDepartmentsForDropdownAsync();
        PositionOptions = await _employeeService.GetPositionsForDropdownAsync();
        ProfileDynamicSections = await EmployeeProfileDynamicFields.LoadSectionsAsync(_dbContext, 0);
        await LoadLookupsAsync();
        FieldSettings = await EmployeeFieldControl.GetSettingsAsync(_dbContext);
        RequiredFieldKeys = EmployeeFieldControl.RequiredKeys(FieldSettings);
        await LoadEmployeeNameLanguagesAsync(preservePostedValues: false);

        var codeSchema = await EmployeeCodeSchema.GetAsync(_dbContext);
        CodeSchemaActive = codeSchema?.IsActive == true;
        if (codeSchema is { IsActive: true })
        {
            CodeSchemaPreview = codeSchema.Prefix + (codeSchema.LastNumber + 1).ToString(new string('0', Math.Clamp(codeSchema.Digits, 1, 12)));
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Branches = await _employeeService.GetBranchesForDropdownAsync();
        await ResolveSelectedCompanyAsync();
        Departments = await _employeeService.GetDepartmentsForDropdownAsync();
        PositionOptions = await _employeeService.GetPositionsForDropdownAsync();
        ProfileDynamicSections = await EmployeeProfileDynamicFields.LoadSectionsAsync(_dbContext, 0);
        await LoadLookupsAsync();
        FieldSettings = await EmployeeFieldControl.GetSettingsAsync(_dbContext);
        RequiredFieldKeys = EmployeeFieldControl.RequiredKeys(FieldSettings);
        await LoadEmployeeNameLanguagesAsync(preservePostedValues: true);

        // رمز الموظف: إن تُرك فارغاً والمخطط مفعّل → توليد ذرّي (زيادة التسلسل بنفس العبارة).
        var postSchema = await EmployeeCodeSchema.GetAsync(_dbContext);
        CodeSchemaActive = postSchema?.IsActive == true;
        if (string.IsNullOrWhiteSpace(Employee.EmployeeNo) && CodeSchemaActive)
        {
            var generated = await EmployeeCodeSchema.GenerateNextAsync(_dbContext);
            if (!string.IsNullOrWhiteSpace(generated))
            {
                Employee.EmployeeNo = generated;
                ModelState.Remove("Employee.EmployeeNo");
            }
        }

        await ValidateAndMapEmployeeNamesAsync();

        // التحكم بالحقول: فرض الإلزامية المركزية بالسيرفر.
        EmployeeFieldControl.ValidateRequired(Employee, RequiredFieldKeys, ModelState, "Employee");

        if (!ModelState.IsValid)
            return Page();

        // اشتقاق المواطنة من الجنسية إن كانت القاعدة مفعّلة (تهيئة الأشخاص) —
        // تُطبَّق بالسيرفر كي لا تتوقف على JS الواجهة.
        if (!string.IsNullOrWhiteSpace(Employee.Nationality) &&
            bool.TryParse(await HrSettingsStore.GetAsync(_dbContext, CitizenshipPolicy.KeyEnabled, "False"), out var citizenshipRule) &&
            citizenshipRule)
        {
            var citizenList = await HrSettingsStore.GetAsync(
                _dbContext, CitizenshipPolicy.KeyNationalities, CitizenshipPolicy.DefaultNationalities);
            Employee.IsCitizen = CitizenshipPolicy.IsCitizen(Employee.Nationality, citizenList);
        }

        // حساب الدخول إجباريّ لكل موظف جديد: نتحقّق قبل إنشاء الموظف كي لا نُنشئ
        // موظفاً ثم نفشل. اسم الدخول اختياري (افتراضياً = كود الموظف)؛ الكلمة مطلوبة.
        CreateLoginAccount = true;
        string? loginUsername = null;
        if (CreateLoginAccount)
        {

            loginUsername = string.IsNullOrWhiteSpace(LoginUsername)
                ? Employee.EmployeeNo?.Trim()
                : LoginUsername.Trim();

            if (string.IsNullOrWhiteSpace(loginUsername))
            {
                ErrorMessage = "اسم الدخول مطلوب لإنشاء حساب الموظف.";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(LoginPassword) || LoginPassword.Length < 8)
            {
                ErrorMessage = "كلمة مرور الدخول يجب ألا تقل عن 8 أحرف.";
                return Page();
            }

            var takenCount = await HrmsDatabase.ScalarAsync<int>(
                _dbContext,
                "SELECT COUNT(*) FROM AppLoginUsers WHERE Username = @Username;",
                command => HrmsDatabase.AddParameter(command, "@Username", loginUsername));

            if (takenCount > 0)
            {
                ErrorMessage = $"اسم الدخول «{loginUsername}» مستخدم مسبقاً. اختر اسماً آخر.";
                return Page();
            }
        }

        var created = await _employeeService.CreateAsync(Employee);

        if (!created)
        {
            ErrorMessage = "\u062a\u0639\u0630\u0631 \u0625\u0646\u0634\u0627\u0621 \u0627\u0644\u0645\u0648\u0638\u0641. \u062a\u0623\u0643\u062f \u0645\u0646 \u0643\u0648\u062f \u0627\u0644\u0645\u0648\u0638\u0641 \u0648\u0645\u0648\u0642\u0639 \u0627\u0644\u0639\u0645\u0644 \u0648\u0627\u0644\u0642\u0633\u0645 \u0648\u0623\u0646\u0647\u0627 \u062a\u062a\u0628\u0639 \u0625\u0644\u0649 \u0646\u0641\u0633 \u0627\u0644\u0634\u0631\u0643\u0629.";
            return Page();
        }

        var employeeId = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT TOP 1 Id FROM Employees WHERE EmployeeNo = @EmployeeNo ORDER BY Id DESC",
            command => HrmsDatabase.AddParameter(command, "@EmployeeNo", Employee.EmployeeNo));

        if (employeeId > 0)
        {
            await SaveEmployeeNameTranslationsAsync(employeeId);
            await EmployeeProfileDynamicFields.SaveAsync(_dbContext, employeeId, Request.Form);
            var photoResult = await SaveEmployeePhotoAsync(employeeId);
            var documentResult = await SaveInitialDocumentsAsync(employeeId);
            var loginResult = await CreateEmployeeLoginAsync(employeeId, loginUsername);
            var extraResult = string.Join(" ", new[] { photoResult, documentResult, loginResult }.Where(x => !string.IsNullOrWhiteSpace(x)));

            if (!string.IsNullOrWhiteSpace(extraResult))
            {
                TempData["SuccessMessage"] = $"\u062a\u0645 \u0625\u0646\u0634\u0627\u0621 \u0627\u0644\u0645\u0648\u0638\u0641 \u0628\u0646\u062c\u0627\u062d. {extraResult}";
            }
            else
            {
                TempData["SuccessMessage"] = "\u062a\u0645 \u0625\u0646\u0634\u0627\u0621 \u0627\u0644\u0645\u0648\u0638\u0641 \u0628\u0646\u062c\u0627\u062d.";
            }
        }
        else
        {
            TempData["SuccessMessage"] = "\u062a\u0645 \u0625\u0646\u0634\u0627\u0621 \u0627\u0644\u0645\u0648\u0638\u0641 \u0628\u0646\u062c\u0627\u062d.";
        }

        return RedirectToPage("./Index");
    }

    private async Task LoadEmployeeNameLanguagesAsync(bool preservePostedValues)
    {
        var posted = preservePostedValues
            ? EmployeeNameTranslations.ToDictionary(
                item => (item.CompanyId, item.CultureCode),
                item => item,
                EmployeeNameTranslationKeyComparer.Instance)
            : new Dictionary<(int CompanyId, string CultureCode), EmployeeNameTranslationInput>(
                EmployeeNameTranslationKeyComparer.Instance);

        var result = new List<EmployeeNameTranslationInput>();
        CompaniesMissingLanguageSetup = [];
        foreach (var companyId in Branches.Select(item => item.CompanyId).Where(id => id > 0).Distinct())
        {
            var languages = await _dataLocalization.GetLanguagesAsync(
                companyId,
                HttpContext.RequestAborted);
            if (languages.Count == 0)
            {
                CompaniesMissingLanguageSetup.Add(companyId);
                continue;
            }

            // إنشاء الموظف يجمع الاسم باللغة الأساسية فقط. اللغات الإضافية
            // تُستكمل لاحقاً من شاشة الترجمات المستقلة ولا تُحمّل هذا النموذج.
            var primaryLanguage = languages.FirstOrDefault(item => item.IsDefault) ?? languages[0];
            posted.TryGetValue((companyId, primaryLanguage.CultureCode), out var existing);
            result.Add(new EmployeeNameTranslationInput
            {
                CompanyId = companyId,
                CultureCode = primaryLanguage.CultureCode,
                NativeName = primaryLanguage.NativeName,
                Direction = primaryLanguage.Direction,
                IsDefault = true,
                FirstName = existing?.FirstName,
                SecondName = existing?.SecondName,
                ThirdName = existing?.ThirdName,
                LastName = existing?.LastName
            });
        }

        EmployeeNameTranslations = result;
    }

    private async Task ResolveSelectedCompanyAsync()
    {
        var branches = Branches.ToList();
        Branches = branches;
        var companyIds = branches
            .Select(item => item.CompanyId)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
        CompanyOptions = await _dbContext.Companies
            .AsNoTracking()
            .Where(item => companyIds.Contains(item.Id) && item.IsActive && !item.IsDeleted)
            .OrderBy(item => item.Name)
            .Select(item => new EmployeeCompanyChoice(item.Id, item.Name))
            .ToListAsync(HttpContext.RequestAborted);

        SelectedCompanyId = CompanySelectionContext.Resolve(
            HttpContext,
            SelectedCompanyId,
            CompanyOptions.Select(item => item.Id).ToArray());
    }

    private async Task ValidateAndMapEmployeeNamesAsync()
    {
        var companyId = await _dbContext.Branches
            .AsNoTracking()
            .Where(item => item.Id == Employee.BranchId && item.IsActive && !item.IsDeleted)
            .Select(item => (int?)item.CompanyId)
            .FirstOrDefaultAsync(HttpContext.RequestAborted);
        if (companyId is null)
        {
            ModelState.AddModelError("Employee.BranchId", "موقع العمل غير موجود أو غير فعال.");
            return;
        }

        if (SelectedCompanyId != companyId)
        {
            ModelState.AddModelError(
                nameof(SelectedCompanyId),
                "موقع العمل يجب أن يكون تابعاً للشركة المحددة في البيانات الأساسية.");
            return;
        }

        var companyValues = EmployeeNameTranslations
            .Where(item => item.CompanyId == companyId.Value)
            .ToArray();
        var languages = await _dataLocalization.GetLanguagesAsync(companyId.Value, HttpContext.RequestAborted);
        var primaryLanguage = languages.FirstOrDefault(item => item.IsDefault) ?? languages.FirstOrDefault();
        var source = primaryLanguage is null
            ? null
            : companyValues.FirstOrDefault(item =>
                string.Equals(item.CultureCode, primaryLanguage.CultureCode, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            ModelState.AddModelError(nameof(EmployeeNameTranslations), "تعذر تحديد اللغة الأساسية لاسم الموظف.");
            return;
        }

        if (string.IsNullOrWhiteSpace(source.FirstName))
            ModelState.AddModelError(nameof(EmployeeNameTranslations), "الاسم الأول مطلوب باللغة الأساسية.");
        if (string.IsNullOrWhiteSpace(source.LastName))
            ModelState.AddModelError(nameof(EmployeeNameTranslations), "اللقب مطلوب باللغة الأساسية.");
        if (!ModelState.IsValid) return;

        Employee.FirstName = source.FirstName?.Trim();
        Employee.SecondName = source.SecondName?.Trim();
        Employee.ThirdName = source.ThirdName?.Trim();
        Employee.LastName = source.LastName?.Trim();
        Employee.FullName = ComposeName(source);

        var english = companyValues.FirstOrDefault(item =>
            item.CultureCode.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        Employee.FirstNameEn = english?.FirstName?.Trim();
        Employee.SecondNameEn = english?.SecondName?.Trim();
        Employee.ThirdNameEn = english?.ThirdName?.Trim();
        Employee.LastNameEn = english?.LastName?.Trim();

        foreach (var key in new[]
                 {
                     "Employee.FullName", "Employee.FirstName", "Employee.SecondName",
                     "Employee.ThirdName", "Employee.LastName", "Employee.FirstNameEn",
                     "Employee.SecondNameEn", "Employee.ThirdNameEn", "Employee.LastNameEn"
                 })
            ModelState.Remove(key);
    }

    private async Task SaveEmployeeNameTranslationsAsync(int employeeId)
    {
        var companyId = await _dbContext.Employees
            .AsNoTracking()
            .Where(item => item.Id == employeeId)
            .Select(item => item.CompanyId)
            .FirstAsync(HttpContext.RequestAborted);
        if (companyId is not { } scopedCompanyId) return;

        var values = ToLocalizedNameValues(
            EmployeeNameTranslations.Where(item => item.CompanyId == scopedCompanyId),
            includeFullName: true);
        await _dataLocalization.SaveValuesAsync(
            scopedCompanyId,
            "Employee",
            employeeId,
            values,
            HttpContext.RequestAborted);
    }

    private static List<LocalizedFieldValue> ToLocalizedNameValues(
        IEnumerable<EmployeeNameTranslationInput> translations,
        bool includeFullName)
    {
        var values = new List<LocalizedFieldValue>();
        foreach (var item in translations)
        {
            values.Add(new(item.CultureCode, "FirstName", item.FirstName));
            values.Add(new(item.CultureCode, "SecondName", item.SecondName));
            values.Add(new(item.CultureCode, "ThirdName", item.ThirdName));
            values.Add(new(item.CultureCode, "LastName", item.LastName));
            if (includeFullName) values.Add(new(item.CultureCode, "FullName", ComposeName(item)));
        }
        return values;
    }

    private static string ComposeName(EmployeeNameTranslationInput item) => string.Join(' ',
        new[] { item.FirstName, item.SecondName, item.ThirdName, item.LastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));


    /// <summary>
    /// ينشئ حساب دخول «موظف» للموظف الجديد إن طلب الأدمن ذلك. اسم المستخدم وكلمة
    /// المرور مُتحقَّقان مسبقاً (فريدان و≥8)؛ يُهاش بملحٍ مستقلّ ويُوسَم بإجبار
    /// التغيير حسب الاختيار. الدور «Employee» دائماً — أدنى صلاحية.
    /// </summary>
    private async Task<string> CreateEmployeeLoginAsync(int employeeId, string? username)
    {
        if (!CreateLoginAccount || string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(LoginPassword))
        {
            return string.Empty;
        }

        var salt = SimplePasswordHasher.CreateSalt();
        var hash = SimplePasswordHasher.HashPassword(LoginPassword, salt);
        var actor = User?.Identity?.Name ?? "HR";
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var loginId = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            """
INSERT INTO AppLoginUsers
(EmployeeId, Username, PasswordHash, PasswordSalt, Role, IsActive,
 PasswordChangedAt, MustChangePassword, CreatedAt)
VALUES
(@EmployeeId, @Username, @PasswordHash, @PasswordSalt, 'Employee', 1,
 SYSUTCDATETIME(), @MustChange, SYSUTCDATETIME());

SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Username", username);
                HrmsDatabase.AddParameter(command, "@PasswordHash", hash);
                HrmsDatabase.AddParameter(command, "@PasswordSalt", salt);
                HrmsDatabase.AddParameter(command, "@MustChange", LoginForceChange);
            });

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('UnifiedIdentity', @EntityId, 'Create Employee Login On Employee Create', @NewValues, @Actor, @IpAddress);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EntityId", $"{loginId}:0");
                HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(
                    ("LoginId", loginId),
                    ("EmployeeId", employeeId),
                    ("UserName", username),
                    ("Role", "Employee"),
                    ("MustChangePassword", LoginForceChange)));
                HrmsDatabase.AddParameter(command, "@Actor", actor);
                HrmsDatabase.AddParameter(command, "@IpAddress", ipAddress);
            });

        return $"وأُنشئ حساب دخول باسم «{username}».";
    }

    private async Task<string> SaveEmployeePhotoAsync(int employeeId)
    {
        if (EmployeePhoto == null || EmployeePhoto.Length == 0)
            return string.Empty;

        var extension = Path.GetExtension(EmployeePhoto.FileName);

        if (string.IsNullOrWhiteSpace(extension) || !AllowedEmployeePhotoExtensions.Contains(extension))
            return "\u0644\u0645 \u064a\u062a\u0645 \u062d\u0641\u0638 \u0635\u0648\u0631\u0629 \u0627\u0644\u0645\u0648\u0638\u0641 \u0644\u0623\u0646 \u0627\u0644\u0635\u064a\u063a\u0629 \u063a\u064a\u0631 \u0645\u062f\u0639\u0648\u0645\u0629.";

        if (EmployeePhoto.Length > 5 * 1024 * 1024)
            return "\u0644\u0645 \u064a\u062a\u0645 \u062d\u0641\u0638 \u0635\u0648\u0631\u0629 \u0627\u0644\u0645\u0648\u0638\u0641 \u0644\u0623\u0646 \u062d\u062c\u0645\u0647\u0627 \u0623\u0643\u0628\u0631 \u0645\u0646 5MB.";

        var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "employee-photos");
        Directory.CreateDirectory(uploadRoot);

        var storedName = $"employee_{employeeId}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
        var physicalPath = Path.Combine(uploadRoot, storedName);
        var relativePath = $"/uploads/employee-photos/{storedName}";

        await using (var stream = System.IO.File.Create(physicalPath))
        {
            await EmployeePhoto.CopyToAsync(stream);
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            "UPDATE Employees SET PhotoPath = @PhotoPath WHERE Id = @EmployeeId;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@PhotoPath", relativePath);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
            });

        return "\u062a\u0645 \u062d\u0641\u0638 \u0635\u0648\u0631\u0629 \u0627\u0644\u0645\u0648\u0638\u0641.";
    }

    private async Task<string> SaveInitialDocumentsAsync(int employeeId)
    {
        if (InitialDocumentFiles == null || InitialDocumentFiles.Count == 0)
            return string.Empty;

        var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "employee-documents");
        Directory.CreateDirectory(uploadRoot);

        var savedCount = 0;
        var skippedCount = 0;

        for (var i = 0; i < InitialDocumentFiles.Count; i++)
        {
            var file = InitialDocumentFiles[i];

            if (file == null || file.Length == 0)
            {
                skippedCount++;
                continue;
            }

            var extension = Path.GetExtension(file.FileName);

            if (string.IsNullOrWhiteSpace(extension) || !AllowedDocumentExtensions.Contains(extension))
            {
                skippedCount++;
                continue;
            }

            if (file.Length > 10 * 1024 * 1024)
            {
                skippedCount++;
                continue;
            }

            var documentType = InitialDocumentTypes.Count > i && !string.IsNullOrWhiteSpace(InitialDocumentTypes[i])
                ? InitialDocumentTypes[i].Trim()
                : "Other";

            var requiredText = InitialDocumentRequired.Count > i && !string.IsNullOrWhiteSpace(InitialDocumentRequired[i])
                ? InitialDocumentRequired[i].Trim()
                : "Optional";

            var safeOriginalName = Path.GetFileName(file.FileName);
            var storedName = $"{employeeId}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
            var physicalPath = Path.Combine(uploadRoot, storedName);
            var relativePath = $"/uploads/employee-documents/{storedName}";

            await using (var stream = System.IO.File.Create(physicalPath))
            {
                await file.CopyToAsync(stream);
            }

            await HrmsDatabase.ExecuteAsync(
                _dbContext,
                """
INSERT INTO EmployeeDocuments
(EmployeeId, DocumentType, FileName, StoredPath, ExpiryDate, Notes, UploadedBy)
VALUES
(@EmployeeId, @DocumentType, @FileName, @StoredPath, NULL, @Notes, @UploadedBy);

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('EmployeeDocument', CAST(@EmployeeId AS nvarchar(80)), 'Upload Document On Create', @NewValues, @UploadedBy, @IpAddress);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                    HrmsDatabase.AddParameter(command, "@DocumentType", documentType);
                    HrmsDatabase.AddParameter(command, "@FileName", safeOriginalName);
                    HrmsDatabase.AddParameter(command, "@StoredPath", relativePath);
                    HrmsDatabase.AddParameter(command, "@Notes", $"Uploaded during employee creation. Required: {requiredText}.");
                    HrmsDatabase.AddParameter(command, "@UploadedBy", User?.Identity?.Name ?? "HR");
                    HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(
                        ("DocumentType", documentType),
                        ("Required", requiredText),
                        ("FileName", safeOriginalName),
                        ("StoredPath", relativePath)));
                    HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
                });

            savedCount++;
        }

        if (savedCount == 0)
            return skippedCount > 0 ? "\u0644\u0645 \u064a\u062a\u0645 \u062d\u0641\u0638 \u0623\u064a \u0645\u0633\u062a\u0645\u0633\u0643 \u0644\u0623\u0646 \u0627\u0644\u0645\u0644\u0641\u0627\u062a \u063a\u064a\u0631 \u0635\u0627\u0644\u062d\u0629 \u0623\u0648 \u0641\u0627\u0631\u063a\u0629." : string.Empty;

        return skippedCount > 0
            ? $"\u062a\u0645 \u062d\u0641\u0638 {savedCount} \u0645\u0633\u062a\u0645\u0633\u0643\u060c \u0648\u062a\u0645 \u062a\u062c\u0627\u0648\u0632 {skippedCount} \u0645\u0644\u0641 \u063a\u064a\u0631 \u0635\u0627\u0644\u062d."
            : $"\u062a\u0645 \u062d\u0641\u0638 {savedCount} \u0645\u0633\u062a\u0645\u0633\u0643.";
    }
}

public sealed class EmployeeNameTranslationInput
{
    public int CompanyId { get; set; }
    public string CultureCode { get; set; } = string.Empty;
    public string NativeName { get; set; } = string.Empty;
    public string Direction { get; set; } = "ltr";
    public bool IsDefault { get; set; }
    public string? FirstName { get; set; }
    public string? SecondName { get; set; }
    public string? ThirdName { get; set; }
    public string? LastName { get; set; }
}

public sealed record EmployeeCompanyChoice(int Id, string Name);

internal sealed class EmployeeNameTranslationKeyComparer
    : IEqualityComparer<(int CompanyId, string CultureCode)>
{
    public static EmployeeNameTranslationKeyComparer Instance { get; } = new();

    public bool Equals(
        (int CompanyId, string CultureCode) x,
        (int CompanyId, string CultureCode) y) =>
        x.CompanyId == y.CompanyId &&
        string.Equals(x.CultureCode, y.CultureCode, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode((int CompanyId, string CultureCode) value) =>
        HashCode.Combine(value.CompanyId, StringComparer.OrdinalIgnoreCase.GetHashCode(value.CultureCode));
}
