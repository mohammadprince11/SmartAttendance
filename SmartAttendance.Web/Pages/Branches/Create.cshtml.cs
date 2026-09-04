using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Branches.Services;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Companies.ViewModels;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Localization;

namespace SmartAttendance.Web.Pages.Branches;

public class CreateModel : PageModel
{
    private readonly IBranchService _branchService;
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyDataLocalizationService _dataLocalization;

    public CreateModel(
        IBranchService branchService,
        ApplicationDbContext dbContext,
        ICompanyDataLocalizationService dataLocalization)
    {
        _branchService = branchService;
        _dbContext = dbContext;
        _dataLocalization = dataLocalization;
    }

    [BindProperty]
    public BranchCreateViewModel Branch { get; set; } = new();

    [BindProperty]
    public List<BranchTranslationInput> Translations { get; set; } = [];

    public IEnumerable<CompanyListViewModel> Companies { get; set; } = new List<CompanyListViewModel>();

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        Companies = await _branchService.GetCompaniesForDropdownAsync();
        ModelState.Remove("Branch.Code");
        var companies = Companies.ToList();
        if (Branch.CompanyId <= 0 && companies.Count == 1)
            Branch.CompanyId = companies[0].Id;
        await LoadTranslationsAsync(false);
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
        var created = await _branchService.CreateAsync(Branch);

        if (!created)
        {
            ErrorMessage = "Branch code already exists or selected company is invalid.";
            return Page();
        }

        var branchId = await _dbContext.Branches
            .Where(item => item.CompanyId == Branch.CompanyId && item.Name == Branch.Name && !item.IsDeleted)
            .OrderByDescending(item => item.Id)
            .Select(item => item.Id)
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        await _dataLocalization.SaveValuesAsync(
            Branch.CompanyId,
            "Branch",
            branchId,
            ToLocalizedValues(),
            HttpContext.RequestAborted);
        await transaction.CommitAsync(HttpContext.RequestAborted);

        TempData["SuccessMessage"] = "Branch created successfully.";

        return RedirectToPage("./Index");
    }

    private async Task LoadTranslationsAsync(bool preservePostedValues)
    {
        var posted = preservePostedValues
            ? Translations.ToDictionary(item => $"{item.CompanyId}:{item.CultureCode}", StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, BranchTranslationInput>(StringComparer.OrdinalIgnoreCase);
        var result = new List<BranchTranslationInput>();
        foreach (var company in Companies)
        {
            var languages = await _dataLocalization.GetLanguagesAsync(company.Id, HttpContext.RequestAborted);
            foreach (var language in languages)
            {
                posted.TryGetValue($"{company.Id}:{language.CultureCode}", out var value);
                result.Add(new BranchTranslationInput
                {
                    CompanyId = company.Id,
                    CultureCode = language.CultureCode,
                    NativeName = language.NativeName,
                    Direction = language.Direction,
                    IsDefault = language.IsDefault,
                    IsRequired = language.IsRequired,
                    Name = value?.Name,
                    Address = value?.Address
                });
            }
        }
        Translations = result;
    }

    private async Task ValidateAndMapAsync()
    {
        if (Branch.CompanyId <= 0) return;
        var companyValues = Translations.Where(item => item.CompanyId == Branch.CompanyId).ToList();
        var errors = await _dataLocalization.ValidateRequiredValuesAsync(
            Branch.CompanyId,
            new[] { "Name" },
            ToLocalizedValues(companyValues),
            HttpContext.RequestAborted);
        foreach (var error in errors) ModelState.AddModelError(nameof(Translations), error);

        var primary = companyValues.FirstOrDefault(item => item.IsDefault) ?? companyValues.FirstOrDefault();
        if (primary is null || string.IsNullOrWhiteSpace(primary.Name)) return;
        Branch.Name = primary.Name.Trim();
        Branch.Address = string.IsNullOrWhiteSpace(primary.Address) ? null : primary.Address.Trim();
    }

    private List<LocalizedFieldValue> ToLocalizedValues() =>
        ToLocalizedValues(Translations.Where(item => item.CompanyId == Branch.CompanyId));

    private static List<LocalizedFieldValue> ToLocalizedValues(IEnumerable<BranchTranslationInput> values) =>
        values.SelectMany(item => new[]
        {
            new LocalizedFieldValue(item.CultureCode, "Name", item.Name),
            new LocalizedFieldValue(item.CultureCode, "Address", item.Address)
        }).ToList();
}
