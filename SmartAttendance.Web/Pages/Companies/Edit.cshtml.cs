using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Companies.Services;
using SmartAttendance.Application.Companies.ViewModels;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Localization;

namespace SmartAttendance.Web.Pages.Companies;

public class EditModel : PageModel
{
    private readonly ICompanyService _companyService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyDataLocalizationService _dataLocalization;

    public EditModel(
        ICompanyService companyService,
        ApplicationDbContext dbContext,
        ICompanyDataLocalizationService dataLocalization)
    {
        _companyService = companyService;
        _dbContext = dbContext;
        _dataLocalization = dataLocalization;
    }

    [BindProperty]
    public CompanyEditViewModel Company { get; set; } = new();

    [BindProperty]
    public List<CompanyNameTranslationInput> NameTranslations { get; set; } = [];

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var company = await _companyService.GetEditByIdAsync(id);

        if (company == null)
            return NotFound();

        Company = company;
        await LoadTranslationsAsync(false);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ModelState.Remove("Company.Name");
        await LoadTranslationsAsync(true);
        await ValidateAndMapAsync();
        if (!ModelState.IsValid)
            return Page();

        var updated = await _companyService.UpdateAsync(Company);

        if (!updated)
        {
            ErrorMessage = "Company not found or could not be updated.";
            return Page();
        }

        await _dataLocalization.SaveValuesAsync(
            Company.Id,
            "Company",
            Company.Id,
            NameTranslations.Select(item => new LocalizedFieldValue(item.CultureCode, "Name", item.Name)).ToList(),
            HttpContext.RequestAborted);

        TempData["SuccessMessage"] = "Company updated successfully.";

        return RedirectToPage("./Index");
    }

    private async Task LoadTranslationsAsync(bool preservePostedValues)
    {
        var posted = preservePostedValues
            ? NameTranslations.ToDictionary(item => item.CultureCode, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, CompanyNameTranslationInput>(StringComparer.OrdinalIgnoreCase);
        var languages = await _dataLocalization.GetLanguagesAsync(Company.Id, HttpContext.RequestAborted);
        var stored = preservePostedValues
            ? []
            : await _dbContext.LocalizedEntityValues.AsNoTracking()
                .Where(item => item.CompanyId == Company.Id && item.EntityType == "Company" &&
                    item.EntityId == Company.Id && item.FieldName == "Name" && !item.IsDeleted)
                .ToListAsync(HttpContext.RequestAborted);
        NameTranslations = languages.Select(language =>
        {
            posted.TryGetValue(language.CultureCode, out var submitted);
            var value = submitted?.Name ?? stored.FirstOrDefault(item => item.CultureCode == language.CultureCode)?.Value;
            if (language.IsDefault) value ??= Company.Name;
            return new CompanyNameTranslationInput
            {
                CultureCode = language.CultureCode,
                NativeName = language.NativeName,
                Direction = language.Direction,
                IsDefault = language.IsDefault,
                IsRequired = language.IsRequired,
                Name = value
            };
        }).ToList();
    }

    private async Task ValidateAndMapAsync()
    {
        var errors = await _dataLocalization.ValidateRequiredValuesAsync(
            Company.Id,
            new[] { "Name" },
            NameTranslations.Select(item => new LocalizedFieldValue(item.CultureCode, "Name", item.Name)).ToList(),
            HttpContext.RequestAborted);
        foreach (var error in errors) ModelState.AddModelError(nameof(NameTranslations), error);
        var primary = NameTranslations.FirstOrDefault(item => item.IsDefault) ?? NameTranslations.FirstOrDefault();
        if (primary is not null && !string.IsNullOrWhiteSpace(primary.Name)) Company.Name = primary.Name.Trim();
    }
}

public sealed class CompanyNameTranslationInput
{
    public string CultureCode { get; set; } = string.Empty;
    public string NativeName { get; set; } = string.Empty;
    public string Direction { get; set; } = "ltr";
    public bool IsDefault { get; set; }
    public bool IsRequired { get; set; }
    public string? Name { get; set; }
}
