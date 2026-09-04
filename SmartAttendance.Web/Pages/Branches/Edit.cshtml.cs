using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Branches.Services;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Companies.ViewModels;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Localization;

namespace SmartAttendance.Web.Pages.Branches;

public class EditModel : PageModel
{
    private readonly IBranchService _branchService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyDataLocalizationService _dataLocalization;

    public EditModel(
        IBranchService branchService,
        ApplicationDbContext dbContext,
        ICompanyDataLocalizationService dataLocalization)
    {
        _branchService = branchService;
        _dbContext = dbContext;
        _dataLocalization = dataLocalization;
    }

    [BindProperty]
    public BranchEditViewModel Branch { get; set; } = new();

    [BindProperty]
    public List<BranchTranslationInput> Translations { get; set; } = [];

    public IEnumerable<CompanyListViewModel> Companies { get; set; } = new List<CompanyListViewModel>();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Companies = await _branchService.GetCompaniesForDropdownAsync();
        ModelState.Remove("Branch.Code");

        var branch = await _branchService.GetEditByIdAsync(id);

        if (branch == null)
            return NotFound();

        Branch = branch;

        await LoadTranslationsAsync(false);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Companies = await _branchService.GetCompaniesForDropdownAsync();
        ModelState.Remove("Branch.Code");
        ModelState.Remove("Branch.Name");
        ModelState.Remove("Branch.Address");
        await LoadTranslationsAsync(true);
        await ValidateAndMapAsync();

        if (!ModelState.IsValid)
            return Page();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            HttpContext.RequestAborted);
        var updated = await _branchService.UpdateAsync(Branch);

        if (!updated)
        {
            ErrorMessage = "Branch not found, branch code already exists, or selected company is invalid.";
            return Page();
        }

        await _dataLocalization.SaveValuesAsync(
            Branch.CompanyId,
            "Branch",
            Branch.Id,
            ToLocalizedValues(Translations),
            HttpContext.RequestAborted);
        await transaction.CommitAsync(HttpContext.RequestAborted);

        TempData["SuccessMessage"] = "Branch updated successfully.";

        return RedirectToPage("./Index");
    }

    private async Task LoadTranslationsAsync(bool preservePostedValues)
    {
        var posted = preservePostedValues
            ? Translations.ToDictionary(item => item.CultureCode, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, BranchTranslationInput>(StringComparer.OrdinalIgnoreCase);
        var languages = await _dataLocalization.GetLanguagesAsync(Branch.CompanyId, HttpContext.RequestAborted);
        var stored = preservePostedValues
            ? new List<LocalizedEntityValue>()
            : await _dbContext.LocalizedEntityValues.AsNoTracking()
                .Where(item => item.CompanyId == Branch.CompanyId &&
                    item.EntityType == "Branch" && item.EntityId == Branch.Id && !item.IsDeleted)
                .ToListAsync(HttpContext.RequestAborted);

        Translations = languages.Select(language =>
        {
            posted.TryGetValue(language.CultureCode, out var submitted);
            var name = submitted?.Name ?? stored.FirstOrDefault(item =>
                item.CultureCode == language.CultureCode && item.FieldName == "Name")?.Value;
            var address = submitted?.Address ?? stored.FirstOrDefault(item =>
                item.CultureCode == language.CultureCode && item.FieldName == "Address")?.Value;
            if (language.IsDefault)
            {
                name ??= Branch.Name;
                address ??= Branch.Address;
            }
            return new BranchTranslationInput
            {
                CompanyId = Branch.CompanyId,
                CultureCode = language.CultureCode,
                NativeName = language.NativeName,
                Direction = language.Direction,
                IsDefault = language.IsDefault,
                IsRequired = language.IsRequired,
                Name = name,
                Address = address
            };
        }).ToList();
    }

    private async Task ValidateAndMapAsync()
    {
        var errors = await _dataLocalization.ValidateRequiredValuesAsync(
            Branch.CompanyId,
            new[] { "Name" },
            ToLocalizedValues(Translations),
            HttpContext.RequestAborted);
        foreach (var error in errors) ModelState.AddModelError(nameof(Translations), error);
        var primary = Translations.FirstOrDefault(item => item.IsDefault) ?? Translations.FirstOrDefault();
        if (primary is null || string.IsNullOrWhiteSpace(primary.Name)) return;
        Branch.Name = primary.Name.Trim();
        Branch.Address = string.IsNullOrWhiteSpace(primary.Address) ? null : primary.Address.Trim();
    }

    private static List<LocalizedFieldValue> ToLocalizedValues(IEnumerable<BranchTranslationInput> values) =>
        values.SelectMany(item => new[]
        {
            new LocalizedFieldValue(item.CultureCode, "Name", item.Name),
            new LocalizedFieldValue(item.CultureCode, "Address", item.Address)
        }).ToList();
}
