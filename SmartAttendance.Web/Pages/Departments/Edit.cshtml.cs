using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Companies.ViewModels;
using SmartAttendance.Application.Departments.Services;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Localization;

namespace SmartAttendance.Web.Pages.Departments;

public class EditModel : PageModel
{
    private readonly IDepartmentService _departmentService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyDataLocalizationService _dataLocalization;

    public EditModel(
        IDepartmentService departmentService,
        ApplicationDbContext dbContext,
        ICompanyDataLocalizationService dataLocalization)
    {
        _departmentService = departmentService;
        _dbContext = dbContext;
        _dataLocalization = dataLocalization;
    }

    [BindProperty]
    public DepartmentEditViewModel Department { get; set; } = new();

    [BindProperty]
    public List<DepartmentEditNameTranslationInput>
        DepartmentNameTranslations { get; set; } = [];

    public IEnumerable<CompanyListViewModel> Companies { get; set; } =
        new List<CompanyListViewModel>();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Companies =
            (await _departmentService
                .GetCompaniesForDropdownAsync())
            .ToList();

        ModelState.Remove("Department.Code");

        var department =
            await _departmentService
                .GetEditByIdAsync(id);

        if (department is null)
        {
            return NotFound();
        }

        Department = department;

        await LoadDepartmentLanguagesAsync(
            department.Id,
            department.CompanyId,
            department.Name,
            preservePostedValues: false);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Companies =
            (await _departmentService
                .GetCompaniesForDropdownAsync())
            .ToList();

        ModelState.Remove("Department.Code");
        ModelState.Remove("Department.BranchId");
        ModelState.Remove("Department.Name");

        var originalDepartment =
            await _departmentService
                .GetEditByIdAsync(
                    Department.Id);

        if (originalDepartment is null)
        {
            return NotFound();
        }

        await LoadDepartmentLanguagesAsync(
            originalDepartment.Id,
            originalDepartment.CompanyId,
            originalDepartment.Name,
            preservePostedValues: true);

        await ValidateAndMapDepartmentNameAsync();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        await using var transaction =
            await _dbContext.Database
                .BeginTransactionAsync(
                    HttpContext.RequestAborted);

        try
        {
            var updated =
                await _departmentService
                    .UpdateAsync(Department);

            if (!updated)
            {
                await transaction.RollbackAsync(
                    HttpContext.RequestAborted);

                ErrorMessage =
                    "تعذر تحديث القسم. تأكد من الشركة وعدم تكرار الاسم أو الكود.";

                return Page();
            }

            /*
             * إذا نُقل القسم من شركة إلى شركة أخرى،
             * نحذف ترجمات الشركة القديمة حتى لا تبقى
             * بيانات يتيمة مرتبطة بقسم لم يعد تابعاً لها.
             */
            if (originalDepartment.CompanyId !=
                Department.CompanyId)
            {
                var oldTranslations =
                    await _dbContext
                        .LocalizedEntityValues
                        .Where(item =>
                            item.CompanyId ==
                                originalDepartment.CompanyId &&
                            item.EntityType ==
                                "Department" &&
                            item.EntityId ==
                                Department.Id)
                        .ToListAsync(
                            HttpContext.RequestAborted);

                if (oldTranslations.Count > 0)
                {
                    _dbContext
                        .LocalizedEntityValues
                        .RemoveRange(
                            oldTranslations);

                    await _dbContext.SaveChangesAsync(
                        HttpContext.RequestAborted);
                }
            }

            await SaveDepartmentTranslationsAsync(
                Department.Id);

            await transaction.CommitAsync(
                HttpContext.RequestAborted);

            TempData["SuccessMessage"] =
                "تم تحديث القسم وبيانات اللغات بنجاح.";

            return RedirectToPage("./Index");
        }
        catch (Exception exception)
            when (exception is
                InvalidOperationException or
                UnauthorizedAccessException)
        {
            await transaction.RollbackAsync(
                HttpContext.RequestAborted);

            ErrorMessage =
                exception.Message;

            return Page();
        }
    }

    private async Task LoadDepartmentLanguagesAsync(
        int departmentId,
        int originalCompanyId,
        string canonicalName,
        bool preservePostedValues)
    {
        var posted =
            preservePostedValues
                ? DepartmentNameTranslations
                    .ToDictionary(
                        item =>
                            (
                                item.CompanyId,
                                item.CultureCode
                            ),
                        item => item,
                        DepartmentEditNameTranslationKeyComparer
                            .Instance)
                : new Dictionary<
                    (int CompanyId, string CultureCode),
                    DepartmentEditNameTranslationInput>(
                        DepartmentEditNameTranslationKeyComparer
                            .Instance);

        /*
         * نقرأ الترجمات الفعلية للشركة الحالية فقط.
         * اللغات المخفية تبقى محفوظة في DB ولكن
         * لا تظهر في واجهة التعديل.
         */
        var storedTranslations =
            await _dbContext
                .LocalizedEntityValues
                .AsNoTracking()
                .Where(item =>
                    item.CompanyId ==
                        originalCompanyId &&
                    item.EntityType ==
                        "Department" &&
                    item.EntityId ==
                        departmentId &&
                    item.FieldName ==
                        "Name" &&
                    !item.IsDeleted)
                .ToListAsync(
                    HttpContext.RequestAborted);

        var storedByCulture =
            storedTranslations
                .GroupBy(
                    item => item.CultureCode,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderByDescending(
                            item => item.UpdatedAt)
                        .First()
                        .Value,
                    StringComparer.OrdinalIgnoreCase);

        var result =
            new List<DepartmentEditNameTranslationInput>();

        foreach (var company in Companies)
        {
            var languages =
                await _dataLocalization
                    .GetLanguagesAsync(
                        company.Id,
                        HttpContext.RequestAborted);

            foreach (var language in languages)
            {
                posted.TryGetValue(
                    (
                        company.Id,
                        language.CultureCode
                    ),
                    out var postedValue);

                string? value =
                    postedValue?.Name;

                if (!preservePostedValues ||
                    postedValue is null)
                {
                    if (company.Id ==
                        originalCompanyId)
                    {
                        storedByCulture.TryGetValue(
                            language.CultureCode,
                            out value);

                        /*
                         * دعم الأقسام القديمة التي أُنشئت
                         * قبل نظام LocalizedEntityValues.
                         */
                        if (language.IsDefault &&
                            string.IsNullOrWhiteSpace(
                                value))
                        {
                            value =
                                canonicalName;
                        }
                    }
                }

                result.Add(
                    new DepartmentEditNameTranslationInput
                    {
                        CompanyId =
                            company.Id,

                        CultureCode =
                            language.CultureCode,

                        NativeName =
                            language.NativeName,

                        Direction =
                            language.Direction,

                        IsDefault =
                            language.IsDefault,

                        IsRequired =
                            language.IsRequired,

                        Name =
                            value
                    });
            }
        }

        DepartmentNameTranslations =
            result;
    }

    private async Task
        ValidateAndMapDepartmentNameAsync()
    {
        ModelState.Remove(
            "Department.Name");

        if (Department.CompanyId <= 0)
        {
            return;
        }

        if (!Companies.Any(item =>
                item.Id ==
                Department.CompanyId))
        {
            ModelState.AddModelError(
                "Department.CompanyId",
                "الشركة المحددة غير موجودة أو غير متاحة.");

            return;
        }

        var languages =
            await _dataLocalization
                .GetLanguagesAsync(
                    Department.CompanyId,
                    HttpContext.RequestAborted);

        if (languages.Count == 0)
        {
            ModelState.AddModelError(
                nameof(
                    DepartmentNameTranslations),
                "لا توجد لغة بيانات ظاهرة ومفعلة لهذه الشركة.");

            return;
        }

        var companyValues =
            DepartmentNameTranslations
                .Where(item =>
                    item.CompanyId ==
                    Department.CompanyId)
                .ToArray();

        var primaryLanguage =
            languages
                .FirstOrDefault(item =>
                    item.IsDefault)
            ?? languages[0];

        var primaryValue =
            companyValues
                .FirstOrDefault(item =>
                    string.Equals(
                        item.CultureCode,
                        primaryLanguage.CultureCode,
                        StringComparison.OrdinalIgnoreCase));

        if (primaryValue is null)
        {
            ModelState.AddModelError(
                nameof(
                    DepartmentNameTranslations),
                "تعذر تحديد اللغة الأساسية لاسم القسم.");

            return;
        }

        var values =
            ToLocalizedValues(
                companyValues);

        var validationErrors =
            await _dataLocalization
                .ValidateRequiredValuesAsync(
                    Department.CompanyId,
                    ["Name"],
                    values,
                    HttpContext.RequestAborted);

        foreach (var error in validationErrors)
        {
            ModelState.AddModelError(
                nameof(
                    DepartmentNameTranslations),
                error);
        }

        if (!ModelState.IsValid)
        {
            return;
        }

        Department.Name =
            primaryValue.Name?.Trim()
            ?? string.Empty;

        ModelState.Remove(
            "Department.Name");
    }

    private async Task
        SaveDepartmentTranslationsAsync(
            int departmentId)
    {
        var values =
            ToLocalizedValues(
                DepartmentNameTranslations
                    .Where(item =>
                        item.CompanyId ==
                        Department.CompanyId));

        await _dataLocalization
            .SaveValuesAsync(
                Department.CompanyId,
                "Department",
                departmentId,
                values,
                HttpContext.RequestAborted);
    }

    private static List<LocalizedFieldValue>
        ToLocalizedValues(
            IEnumerable<
                DepartmentEditNameTranslationInput>
                    translations)
    {
        return translations
            .Select(item =>
                new LocalizedFieldValue(
                    item.CultureCode,
                    "Name",
                    item.Name))
            .ToList();
    }
}

public sealed class
    DepartmentEditNameTranslationInput
{
    public int CompanyId { get; set; }

    public string CultureCode { get; set; } =
        string.Empty;

    public string NativeName { get; set; } =
        string.Empty;

    public string Direction { get; set; } =
        "ltr";

    public bool IsDefault { get; set; }

    public bool IsRequired { get; set; }

    public string? Name { get; set; }
}

internal sealed class
    DepartmentEditNameTranslationKeyComparer
    : IEqualityComparer<
        (int CompanyId, string CultureCode)>
{
    public static
        DepartmentEditNameTranslationKeyComparer
            Instance { get; } = new();

    public bool Equals(
        (int CompanyId, string CultureCode) x,
        (int CompanyId, string CultureCode) y)
    {
        return
            x.CompanyId == y.CompanyId &&
            string.Equals(
                x.CultureCode,
                y.CultureCode,
                StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(
        (int CompanyId, string CultureCode) value)
    {
        return HashCode.Combine(
            value.CompanyId,
            StringComparer
                .OrdinalIgnoreCase
                .GetHashCode(
                    value.CultureCode));
    }
}