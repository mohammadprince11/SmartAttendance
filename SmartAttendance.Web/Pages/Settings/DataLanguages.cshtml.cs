using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.CompanyContext;
using SmartAttendance.Web.Infrastructure.Localization;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Settings;

[Authorize(Roles = RoleRouteCatalog.Admin)]
public sealed class DataLanguagesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;
    private readonly ILocalizationDictionaryService _dictionary;
    private readonly ICompanyDataLocalizationService _dataLocalization;

    public DataLanguagesModel(
        ApplicationDbContext db,
        ICompanyScopeProvider companyScope,
        ILocalizationDictionaryService dictionary,
        ICompanyDataLocalizationService dataLocalization)
    {
        _db = db;
        _companyScope = companyScope;
        _dictionary = dictionary;
        _dataLocalization = dataLocalization;
    }

    [BindProperty(SupportsGet = true)] public int? CompanyId { get; set; }
    [BindProperty] public string DefaultCultureCode { get; set; } = string.Empty;
    [BindProperty] public List<string> ActiveCultureCodes { get; set; } = [];

    public List<CompanyChoice> Companies { get; private set; } = [];
    public IReadOnlyList<DictionaryLanguage> AvailableLanguages { get; private set; } = [];
    public IReadOnlyList<CompanyLanguageOption> ConfiguredLanguages { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var loadResult = await LoadAsync();
        if (loadResult is not PageResult) return loadResult;
        if (CompanyId is not { } companyId) return NotFound();

        try
        {
            await _dataLocalization.SaveLanguagesAsync(
                companyId,
                DefaultCultureCode,
                ActiveCultureCodes,
                HttpContext.RequestAborted);
            TempData["SuccessMessage"] = "تم حفظ اللغة الأساسية واللغات الإضافية لبيانات الشركة.";
            return RedirectToPage(new { companyId });
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return await LoadAsync();
        }
    }

    private async Task<IActionResult> LoadAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        Companies = (await _db.Companies
                .AsNoTracking()
                .Where(item => !item.IsDeleted && item.IsActive)
                .OrderBy(item => item.Name)
                .Select(item => new CompanyChoice(item.Id, item.Code, item.Name))
                .ToListAsync(HttpContext.RequestAborted))
            .Where(item => scope.Allows(item.Id))
            .ToList();

        CompanyId = CompanySelectionContext.Resolve(
            HttpContext,
            CompanyId,
            Companies.Select(item => item.Id).ToArray());
        if (CompanyId is null) return NotFound();

        AvailableLanguages = await _dictionary.GetLanguagesAsync(HttpContext.RequestAborted);
        ConfiguredLanguages = await _dataLocalization.GetLanguagesAsync(
            CompanyId.Value,
            HttpContext.RequestAborted);

        if (!Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            ActiveCultureCodes = ConfiguredLanguages.Select(item => item.CultureCode).ToList();
            DefaultCultureCode = ConfiguredLanguages.FirstOrDefault(item => item.IsDefault)?.CultureCode
                ?? AvailableLanguages.FirstOrDefault(item => item.IsDefault)?.Code
                ?? AvailableLanguages.FirstOrDefault()?.Code
                ?? string.Empty;
        }

        return Page();
    }

    public sealed record CompanyChoice(int Id, string Code, string Name);
}
