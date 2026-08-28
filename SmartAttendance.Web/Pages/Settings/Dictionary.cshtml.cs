using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Web.Infrastructure.Localization;
using SmartAttendance.Web.Infrastructure.Reports;

namespace SmartAttendance.Web.Pages.Settings;

[Authorize(Roles = "Admin")]
[RequestSizeLimit(8 * 1024 * 1024)]
public sealed class DictionaryModel : PageModel
{
    private const int PageSize = 80;
    private readonly ILocalizationDictionaryService _dictionary;

    public DictionaryModel(ILocalizationDictionaryService dictionary) => _dictionary = dictionary;

    [BindProperty(SupportsGet = true)] public string? Culture { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool MissingOnly { get; set; }
    [BindProperty(SupportsGet = true)] public int P { get; set; } = 1;

    public IReadOnlyList<DictionaryLanguage> Languages { get; private set; } = [];
    public DictionaryLanguage SelectedLanguage { get; private set; } = null!;
    public IReadOnlyList<DictionaryEntryRow> Entries { get; private set; } = [];
    public int TotalEntries { get; private set; }
    public int MissingEntries { get; private set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalEntries / (double)PageSize));

    public async Task<IActionResult> OnGetAsync()
    {
        Languages = await _dictionary.GetLanguagesAsync(HttpContext.RequestAborted);
        if (Languages.Count == 0) return NotFound();
        SelectedLanguage = Languages.FirstOrDefault(item =>
            string.Equals(item.Code, Culture, StringComparison.OrdinalIgnoreCase)) ?? Languages[0];
        Culture = SelectedLanguage.Code;

        var rows = (await _dictionary.GetRowsAsync(HttpContext.RequestAborted))
            .Where(item => string.Equals(item.CultureCode, SelectedLanguage.Code, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        MissingEntries = rows.Count(item => !SelectedLanguage.IsDefault && string.IsNullOrWhiteSpace(item.Translation));

        IEnumerable<DictionaryEntryRow> filtered = rows;
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var query = Q.Trim();
            filtered = filtered.Where(item =>
                item.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Translation.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        if (MissingOnly && !SelectedLanguage.IsDefault)
            filtered = filtered.Where(item => string.IsNullOrWhiteSpace(item.Translation));

        var result = filtered.ToArray();
        TotalEntries = result.Length;
        P = Math.Clamp(P, 1, TotalPages);
        Entries = result.Skip((P - 1) * PageSize).Take(PageSize).ToArray();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(
        string culture,
        string key,
        string translation,
        string? q,
        bool missingOnly,
        int p)
    {
        try
        {
            await _dictionary.SaveTranslationAsync(culture, key, translation, HttpContext.RequestAborted);
            TempData["SuccessMessage"] = "تم حفظ الترجمة وتفعيلها مباشرة.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        return RedirectToPage(new { culture, q, missingOnly, p });
    }

    public async Task<IActionResult> OnPostImportAsync(IFormFile? file, bool replace)
    {
        if (file is null || file.Length == 0)
        {
            TempData["ErrorMessage"] = "اختر ملف Excel أو CSV أولاً.";
            return RedirectToPage(new { culture = Culture });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _dictionary.ImportAsync(stream, file.FileName, replace, HttpContext.RequestAborted);
            TempData["SuccessMessage"] = result.IsNewLanguage
                ? $"تمت إضافة اللغة {result.CultureCode} واستيراد {result.Imported} ترجمة."
                : $"تم تحديث اللغة {result.CultureCode} واستيراد {result.Imported} ترجمة.";
            if (result.Empty > 0)
                TempData["ImportNotice"] = $"يوجد {result.Empty} صفاً بلا ترجمة ويمكن إكمالها من القاموس.";
            return RedirectToPage(new { culture = result.CultureCode });
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            TempData["ErrorMessage"] = exception.Message;
            return RedirectToPage(new { culture = Culture });
        }
    }

    public async Task<IActionResult> OnPostDeleteLanguageAsync(string culture)
    {
        try
        {
            await _dictionary.DeleteLanguageAsync(culture, HttpContext.RequestAborted);
            TempData["SuccessMessage"] = $"تم حذف اللغة {culture} وترجماتها.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetExportAsync(string culture)
    {
        var language = await _dictionary.FindLanguageAsync(culture, HttpContext.RequestAborted);
        if (language is null) return NotFound();
        var rows = (await _dictionary.GetRowsAsync(HttpContext.RequestAborted))
            .Where(item => string.Equals(item.CultureCode, language.Code, StringComparison.OrdinalIgnoreCase))
            .Select(ToExportRow)
            .ToArray();
        return Workbook(rows, $"dictionary-{language.Code}");
    }

    public async Task<IActionResult> OnGetNewLanguageTemplateAsync()
    {
        var rows = (await _dictionary.GetRowsAsync(HttpContext.RequestAborted))
            .Where(item => string.Equals(item.CultureCode, ZynoraSupportedCultures.DefaultCode, StringComparison.OrdinalIgnoreCase))
            .Select(item => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CultureCode"] = "fr-FR",
                ["NativeName"] = "Français",
                ["EnglishName"] = "French",
                ["Direction"] = "ltr",
                ["Key"] = item.Key,
                ["Translation"] = string.Empty
            })
            .ToArray();
        return Workbook(rows, "new-language-template");
    }

    private static Dictionary<string, string> ToExportRow(DictionaryEntryRow item) => new(StringComparer.Ordinal)
    {
        ["CultureCode"] = item.CultureCode,
        ["NativeName"] = item.NativeName,
        ["EnglishName"] = item.EnglishName,
        ["Direction"] = item.Direction,
        ["Key"] = item.Key,
        ["Translation"] = item.Translation
    };

    private FileContentResult Workbook(IReadOnlyList<Dictionary<string, string>> rows, string fileName)
    {
        var columns = new[]
        {
            new ReportExportService.Column("CultureCode", "رمز اللغة"),
            new ReportExportService.Column("NativeName", "اسم اللغة"),
            new ReportExportService.Column("EnglishName", "الاسم بالإنجليزية"),
            new ReportExportService.Column("Direction", "الاتجاه"),
            new ReportExportService.Column("Key", "النص العربي / المفتاح"),
            new ReportExportService.Column("Translation", "الترجمة")
        };
        var export = ReportExportService.Build("xlsx", "القاموس", columns, rows);
        return File(export.Bytes, export.ContentType, $"{fileName}.xlsx");
    }
}
