using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Companies.ViewModels;
using SmartAttendance.Application.Departments.Services;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Localization;

namespace SmartAttendance.Web.Pages.Departments;

public class CreateModel : PageModel
{
    private readonly IDepartmentService _departmentService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyDataLocalizationService _dataLocalization;

    public CreateModel(
        IDepartmentService departmentService,
        ApplicationDbContext dbContext,
        ICompanyDataLocalizationService dataLocalization)
    {
        _departmentService = departmentService;
        _dbContext = dbContext;
        _dataLocalization = dataLocalization;
    }

    [BindProperty]
    public DepartmentCreateViewModel Department { get; set; } = new();

    [BindProperty]
    public List<DepartmentNameTranslationInput> DepartmentNameTranslations { get; set; } = [];

    public IEnumerable<CompanyListViewModel> Companies { get; set; } =
        new List<CompanyListViewModel>();

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        Companies = (await _departmentService
            .GetCompaniesForDropdownAsync())
            .ToList();

        ModelState.Remove("Department.Code");

        var companyList = Companies.ToList();

        if (Department.CompanyId <= 0 && companyList.Count == 1)
        {
            Department.CompanyId = companyList[0].Id;
        }

        await LoadDepartmentLanguagesAsync(
            preservePostedValues: false);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Companies = (await _departmentService
            .GetCompaniesForDropdownAsync())
            .ToList();

        ModelState.Remove("Department.Code");
        ModelState.Remove("Department.BranchId");

        await LoadDepartmentLanguagesAsync(
            preservePostedValues: true);

        await ValidateAndMapDepartmentNameAsync();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                HttpContext.RequestAborted);

        try
        {
            var created =
                await _departmentService.CreateAsync(Department);

            if (!created)
            {
                await transaction.RollbackAsync(
                    HttpContext.RequestAborted);

                ErrorMessage =
                    "تعذر إضافة القسم. تأكد من الشركة وعدم تكرار الاسم أو الكود.";

                return Page();
            }

            var departmentId = await _dbContext.Departments
                .AsNoTracking()
                .Where(item =>
                    item.CompanyId == Department.CompanyId &&
                    item.Name == Department.Name)
                .OrderByDescending(item => item.Id)
                .Select(item => item.Id)
                .FirstOrDefaultAsync(
                    HttpContext.RequestAborted);

            if (departmentId <= 0)
            {
                await transaction.RollbackAsync(
                    HttpContext.RequestAborted);

                ErrorMessage =
                    "تم إنشاء القسم ولكن تعذر تحديد معرفه لحفظ بيانات اللغات.";

                return Page();
            }

            await SaveDepartmentTranslationsAsync(
                departmentId);

            await transaction.CommitAsync(
                HttpContext.RequestAborted);

            TempData["SuccessMessage"] =
                "تمت إضافة القسم وبيانات اللغات بنجاح.";

            return RedirectToPage("./Index");
        }
        catch (Exception exception)
            when (exception is
                InvalidOperationException or
                UnauthorizedAccessException)
        {
            await transaction.RollbackAsync(
                HttpContext.RequestAborted);

            ErrorMessage = exception.Message;

            return Page();
        }
    }

    private async Task LoadDepartmentLanguagesAsync(
        bool preservePostedValues)
    {
        var posted = preservePostedValues
            ? DepartmentNameTranslations.ToDictionary(
                item => (
                    item.CompanyId,
                    item.CultureCode),
                item => item,
                DepartmentNameTranslationKeyComparer.Instance)
            : new Dictionary<
                (int CompanyId, string CultureCode),
                DepartmentNameTranslationInput>(
                    DepartmentNameTranslationKeyComparer.Instance);

        var result =
            new List<DepartmentNameTranslationInput>();

        foreach (var company in Companies)
        {
            var languages =
                await _dataLocalization.GetLanguagesAsync(
                    company.Id,
                    HttpContext.RequestAborted);

            foreach (var language in languages)
            {
                posted.TryGetValue(
                    (
                        company.Id,
                        language.CultureCode),
                    out var existing);

                result.Add(
                    new DepartmentNameTranslationInput
                    {
                        CompanyId = company.Id,
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
                        Name = existing?.Name
                    });
            }
        }

        DepartmentNameTranslations = result;
    }

    private async Task ValidateAndMapDepartmentNameAsync()
    {
        ModelState.Remove("Department.Name");

        if (Department.CompanyId <= 0)
        {
            return;
        }

        if (!Companies.Any(
                item =>
                    item.Id == Department.CompanyId))
        {
            ModelState.AddModelError(
                "Department.CompanyId",
                "الشركة المحددة غير موجودة أو غير فعالة.");

            return;
        }

        var languages =
            await _dataLocalization.GetLanguagesAsync(
                Department.CompanyId,
                HttpContext.RequestAborted);

        if (languages.Count == 0)
        {
            ModelState.AddModelError(
                nameof(DepartmentNameTranslations),
                "يجب تفعيل لغة أساسية واحدة على الأقل لبيانات الشركة.");

            return;
        }

        var companyValues =
            DepartmentNameTranslations
                .Where(item =>
                    item.CompanyId ==
                    Department.CompanyId)
                .ToArray();

        var primaryLanguage =
            languages.FirstOrDefault(
                item => item.IsDefault)
            ?? languages[0];

        var source =
            companyValues.FirstOrDefault(
                item =>
                    string.Equals(
                        item.CultureCode,
                        primaryLanguage.CultureCode,
                        StringComparison.OrdinalIgnoreCase));

        if (source is null)
        {
            ModelState.AddModelError(
                nameof(DepartmentNameTranslations),
                "تعذر تحديد اللغة الأساسية لاسم القسم.");

            return;
        }

        var values =
            ToLocalizedValues(companyValues);

        var errors =
            await _dataLocalization
                .ValidateRequiredValuesAsync(
                    Department.CompanyId,
                    ["Name"],
                    values,
                    HttpContext.RequestAborted);

        foreach (var error in errors)
        {
            ModelState.AddModelError(
                nameof(DepartmentNameTranslations),
                error);
        }

        if (!ModelState.IsValid)
        {
            return;
        }

        Department.Name =
            source.Name?.Trim() ?? string.Empty;

        ModelState.Remove("Department.Name");
    }

    private async Task SaveDepartmentTranslationsAsync(
        int departmentId)
    {
        var values =
            ToLocalizedValues(
                DepartmentNameTranslations
                    .Where(item =>
                        item.CompanyId ==
                        Department.CompanyId));

        await _dataLocalization.SaveValuesAsync(
            Department.CompanyId,
            "Department",
            departmentId,
            values,
            HttpContext.RequestAborted);
    }

    private static List<LocalizedFieldValue>
        ToLocalizedValues(
            IEnumerable<DepartmentNameTranslationInput> translations)
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

public sealed class DepartmentNameTranslationInput
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

internal sealed class DepartmentNameTranslationKeyComparer
    : IEqualityComparer<(int CompanyId, string CultureCode)>
{
    public static DepartmentNameTranslationKeyComparer Instance
        { get; } = new();

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
            StringComparer.OrdinalIgnoreCase
                .GetHashCode(value.CultureCode));
    }
}