using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Companies.ViewModels;
using SmartAttendance.Application.Departments.Services;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Localization;

namespace SmartAttendance.Web.Pages.Departments;

public class IndexModel : PageModel
{
    private readonly IDepartmentService _departmentService;
    private readonly ICompanyDataLocalizationService _dataLocalization;
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(
        IDepartmentService departmentService,
        ICompanyDataLocalizationService dataLocalization,
        ApplicationDbContext dbContext)
    {
        _departmentService = departmentService;
        _dataLocalization = dataLocalization;
        _dbContext = dbContext;
    }

    public IEnumerable<DepartmentListViewModel> Departments { get; set; } =
        new List<DepartmentListViewModel>();

    public IEnumerable<CompanyListViewModel> Companies { get; set; } =
        new List<CompanyListViewModel>();

    public List<DepartmentQuickCreateLanguageInput>
        QuickCreateLanguages { get; set; } = [];

    public List<DepartmentEditPopupPayload>
        EditPayloads { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    public async Task OnGetAsync()
    {
        Departments =
            (await _departmentService
                .GetAllAsync(SearchTerm))
            .ToList();

        Companies =
            (await _departmentService
                .GetCompaniesForDropdownAsync())
            .ToList();

        await LoadCompanyLanguagesAsync();
        await LoadEditPayloadsAsync();
    }

    private async Task LoadCompanyLanguagesAsync()
    {
        QuickCreateLanguages = [];

        foreach (var company in Companies)
        {
            var languages =
                await _dataLocalization
                    .GetLanguagesAsync(
                        company.Id,
                        HttpContext.RequestAborted);

            foreach (var language in languages)
            {
                QuickCreateLanguages.Add(
                    new DepartmentQuickCreateLanguageInput
                    {
                        CompanyId = company.Id,
                        CultureCode = language.CultureCode,
                        NativeName = language.NativeName,
                        Direction = language.Direction,
                        IsDefault = language.IsDefault,
                        IsRequired = language.IsRequired
                    });
            }
        }
    }

    private async Task LoadEditPayloadsAsync()
    {
        var departments =
            Departments.ToList();

        if (departments.Count == 0)
        {
            EditPayloads = [];
            return;
        }

        var departmentIds =
            departments
                .Select(item => item.Id)
                .ToArray();

        var translations =
            await _dbContext
                .LocalizedEntityValues
                .AsNoTracking()
                .Where(item =>
                    departmentIds.Contains(item.EntityId) &&
                    item.EntityType == "Department" &&
                    item.FieldName == "Name" &&
                    !item.IsDeleted)
                .ToListAsync(
                    HttpContext.RequestAborted);

        var translationMap =
            translations
                .GroupBy(item =>
                    $"{item.EntityId}|{item.CultureCode}",
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group =>
                        group
                            .OrderByDescending(item =>
                                item.UpdatedAt)
                            .First()
                            .Value,
                    StringComparer.OrdinalIgnoreCase);

        EditPayloads = [];

        foreach (var department in departments)
        {
            var languages =
                QuickCreateLanguages
                    .Where(item =>
                        item.CompanyId ==
                        department.CompanyId)
                    .ToArray();

            var values =
                new List<DepartmentEditPopupLanguageValue>();

            foreach (var language in languages)
            {
                translationMap.TryGetValue(
                    $"{department.Id}|{language.CultureCode}",
                    out var value);

                if (language.IsDefault &&
                    string.IsNullOrWhiteSpace(value))
                {
                    value = department.Name;
                }

                values.Add(
                    new DepartmentEditPopupLanguageValue
                    {
                        CultureCode =
                            language.CultureCode,

                        Value =
                            value ?? string.Empty
                    });
            }

            EditPayloads.Add(
                new DepartmentEditPopupPayload
                {
                    Id = department.Id,
                    CompanyId = department.CompanyId,
                    IsActive = department.IsActive,
                    Values = values
                });
        }
    }
}

public sealed class DepartmentQuickCreateLanguageInput
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
}

public sealed class DepartmentEditPopupPayload
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public bool IsActive { get; set; }

    public List<DepartmentEditPopupLanguageValue>
        Values { get; set; } = [];
}

public sealed class DepartmentEditPopupLanguageValue
{
    public string CultureCode { get; set; } =
        string.Empty;

    public string Value { get; set; } =
        string.Empty;
}