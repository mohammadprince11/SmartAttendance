using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Localization;
using SmartAttendance.Web.Infrastructure.Reports;

namespace SmartAttendance.Web.Pages.Settings;

[Authorize(Roles = "Admin")]
[RequestSizeLimit(8 * 1024 * 1024)]
public sealed class DictionaryModel : PageModel
{
    private const int PageSize = 80;

    private readonly ILocalizationDictionaryService _dictionary;
    private readonly ILocalizationAutoTranslationService _autoTranslation;
    private readonly ApplicationDbContext _db;

    public DictionaryModel(
        ILocalizationDictionaryService dictionary,
        ILocalizationAutoTranslationService autoTranslation,
        ApplicationDbContext db)
    {
        _dictionary = dictionary;
        _autoTranslation = autoTranslation;
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public string? Culture { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool MissingOnly { get; set; }

    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    public IReadOnlyList<DictionaryLanguage> Languages { get; private set; } = [];
    public DictionaryLanguage SelectedLanguage { get; private set; } = null!;
    public IReadOnlyList<DictionaryEntryRow> Entries { get; private set; } = [];

    public int TotalEntries { get; private set; }
    public int MissingEntries { get; private set; }
    public int MachineGeneratedEntries { get; private set; }

    public bool AutomaticTranslationEnabled =>
        _autoTranslation.IsConfigured;

    public bool SelectedIsSource =>
        SelectedLanguage is not null &&
        string.Equals(
            SelectedLanguage.Code,
            ZynoraSupportedCultures.DefaultCode,
            StringComparison.OrdinalIgnoreCase);

    public int VisibleLanguageCount =>
        Languages.Count(item => !item.IsHidden);

    public int TotalPages =>
        Math.Max(
            1,
            (int)Math.Ceiling(
                TotalEntries / (double)PageSize));

    public async Task<IActionResult> OnGetAsync()
    {
        Languages = await _dictionary.GetAllLanguagesAsync(
            HttpContext.RequestAborted);

        if (Languages.Count == 0)
            return NotFound();

        SelectedLanguage =
            Languages.FirstOrDefault(item =>
                string.Equals(
                    item.Code,
                    Culture,
                    StringComparison.OrdinalIgnoreCase))
            ?? Languages[0];

        Culture = SelectedLanguage.Code;

        var rows =
            (await _dictionary.GetRowsAsync(
                HttpContext.RequestAborted))
            .Where(item =>
                string.Equals(
                    item.CultureCode,
                    SelectedLanguage.Code,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        MissingEntries =
            rows.Count(item =>
                !SelectedIsSource &&
                string.IsNullOrWhiteSpace(
                    item.Translation));

        MachineGeneratedEntries =
            rows.Count(item =>
                item.RequiresReview);

        IEnumerable<DictionaryEntryRow> filtered =
            rows;

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var query = Q.Trim();

            filtered = filtered.Where(item =>
                item.Key.Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase)
                ||
                item.Translation.Contains(
                    query,
                    StringComparison.OrdinalIgnoreCase));
        }

        if (MissingOnly &&
            !SelectedIsSource)
        {
            filtered = filtered.Where(item =>
                string.IsNullOrWhiteSpace(
                    item.Translation));
        }

        var result = filtered.ToArray();

        TotalEntries = result.Length;

        P = Math.Clamp(
            P,
            1,
            TotalPages);

        Entries = result
            .Skip((P - 1) * PageSize)
            .Take(PageSize)
            .ToArray();

        return Page();
    }

    public async Task<IActionResult> OnPostAddLanguageAsync(
        string cultureCode,
        string nativeName,
        string englishName,
        string direction)
    {
        try
        {
            await _dictionary.AddLanguageAsync(
                cultureCode,
                nativeName,
                englishName,
                direction,
                HttpContext.RequestAborted);

            var language =
                await _dictionary.FindLanguageAsync(
                    cultureCode,
                    HttpContext.RequestAborted);

            TempData["SuccessMessage"] =
                $"تمت إضافة اللغة {language?.Code ?? cultureCode} وأصبحت متاحة في قائمة تغيير اللغة.";

            return RedirectToPage(
                new
                {
                    culture =
                        language?.Code ??
                        cultureCode
                });
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] =
                exception.Message;

            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostSetVisibilityAsync(
        string culture,
        bool hidden)
    {
        try
        {
            await _dictionary.SetLanguageHiddenAsync(
                culture,
                hidden,
                HttpContext.RequestAborted);

            TempData["SuccessMessage"] =
                hidden
                    ? $"تم إخفاء اللغة {culture}. بياناتها وترجماتها لم تُحذف."
                    : $"تم إظهار اللغة {culture}.";

            return RedirectToPage(
                new { culture });
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] =
                exception.Message;

            return RedirectToPage(
                new { culture });
        }
    }

    public async Task<IActionResult> OnPostDeleteLanguageAsync(
        string culture)
    {
        try
        {
            var language =
                await _dictionary.FindLanguageAsync(
                    culture,
                    HttpContext.RequestAborted);

            if (language is null)
            {
                TempData["ErrorMessage"] =
                    "اللغة المطلوبة غير موجودة.";

                return RedirectToPage();
            }

            var activeCompanyUsage =
                await _db.CompanyLanguages
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            !item.IsDeleted &&
                            item.IsActive &&
                            item.CultureCode ==
                                language.Code,
                        HttpContext.RequestAborted);

            var localizedDataUsage =
                await _db.LocalizedEntityValues
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            !item.IsDeleted &&
                            item.CultureCode ==
                                language.Code,
                        HttpContext.RequestAborted);

            if (activeCompanyUsage ||
                localizedDataUsage)
            {
                TempData["ErrorMessage"] =
                    "لا يمكن حذف اللغة نهائياً لأنها مستخدمة في بيانات شركة أو بيانات مترجمة. يمكنك إخفاؤها بدلاً من ذلك.";

                return RedirectToPage(
                    new { culture = language.Code });
            }

            await _dictionary.DeleteLanguageAsync(
                language.Code,
                HttpContext.RequestAborted);

            TempData["SuccessMessage"] =
                $"تم حذف اللغة {language.Code} نهائياً.";

            return RedirectToPage();
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] =
                exception.Message;

            return RedirectToPage(
                new { culture });
        }
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
            await _dictionary.SaveTranslationAsync(
                culture,
                key,
                translation,
                HttpContext.RequestAborted);

            TempData["SuccessMessage"] =
                "تم حفظ الترجمة وتفعيلها مباشرة.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] =
                exception.Message;
        }

        return RedirectToPage(
            new
            {
                culture,
                q,
                missingOnly,
                p
            });
    }

    public async Task<IActionResult> OnPostImportAsync(
        IFormFile? file,
        bool replace)
    {
        if (file is null || file.Length == 0)
        {
            TempData["ErrorMessage"] =
                "اختر ملف Excel أو CSV أولاً.";

            return RedirectToPage(
                new { culture = Culture });
        }

        try
        {
            await using var stream =
                file.OpenReadStream();

            var result =
                await _dictionary.ImportAsync(
                    stream,
                    file.FileName,
                    replace,
                    HttpContext.RequestAborted);

            TempData["SuccessMessage"] =
                result.IsNewLanguage
                    ? $"تمت إضافة اللغة {result.CultureCode} واستيراد {result.Imported} ترجمة."
                    : $"تم تحديث اللغة {result.CultureCode} واستيراد {result.Imported} ترجمة.";

            if (result.Empty > 0)
            {
                TempData["ImportNotice"] =
                    $"يوجد {result.Empty} صفاً بلا ترجمة ويمكن إكمالها من القاموس.";
            }

            return RedirectToPage(
                new
                {
                    culture =
                        result.CultureCode
                });
        }
        catch (Exception exception)
            when (exception is
                InvalidOperationException or
                IOException)
        {
            TempData["ErrorMessage"] =
                exception.Message;

            return RedirectToPage(
                new { culture = Culture });
        }
    }

    public async Task<IActionResult> OnPostAutoTranslateAsync(
        string culture,
        int maximumItems = 250)
    {
        try
        {
            var result =
                await _autoTranslation
                    .TranslateMissingAsync(
                        culture,
                        maximumItems,
                        HttpContext.RequestAborted);

            TempData["SuccessMessage"] =
                result.Translated == 0
                    ? "لا توجد ترجمات ناقصة ضمن اللغة المحددة."
                    : $"تمت ترجمة {result.Translated} عبارة آلياً ووُسمت للمراجعة البشرية.";

            if (result.Remaining > 0)
            {
                TempData["ImportNotice"] =
                    result.Warning is null
                        ? $"تبقّى {result.Remaining} عبارة من الدفعة المحددة."
                        : $"توقفت الدفعة بعد حفظ المكتمل. المتبقي {result.Remaining}. السبب: {result.Warning}";
            }
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] =
                exception.Message;
        }

        return RedirectToPage(
            new
            {
                culture,
                missingOnly = true
            });
    }

    public async Task<IActionResult> OnGetExportAsync(
        string culture)
    {
        var language =
            await _dictionary.FindLanguageAsync(
                culture,
                HttpContext.RequestAborted);

        if (language is null)
            return NotFound();

        var rows =
            (await _dictionary.GetRowsAsync(
                HttpContext.RequestAborted))
            .Where(item =>
                string.Equals(
                    item.CultureCode,
                    language.Code,
                    StringComparison.OrdinalIgnoreCase))
            .Select(ToExportRow)
            .ToArray();

        return Workbook(
            rows,
            $"dictionary-{language.Code}");
    }

    public async Task<IActionResult>
        OnGetNewLanguageTemplateAsync()
    {
        var sourceKeys =
            (await _dictionary.GetRowsAsync(
                HttpContext.RequestAborted))
            .Select(item => item.Key)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(
                item => item,
                StringComparer.Ordinal)
            .ToArray();

        var rows =
            sourceKeys
                .Select(key =>
                    new Dictionary<string, string>(
                        StringComparer.Ordinal)
                    {
                        ["CultureCode"] = "fr-FR",
                        ["NativeName"] = "Français",
                        ["EnglishName"] = "French",
                        ["Direction"] = "ltr",
                        ["Key"] = key,
                        ["Translation"] =
                            string.Empty
                    })
                .ToArray();

        return Workbook(
            rows,
            "new-language-template");
    }
    private static Dictionary<string, string>
        ToExportRow(DictionaryEntryRow item) =>
        new(StringComparer.Ordinal)
        {
            ["CultureCode"] =
                item.CultureCode,
            ["NativeName"] =
                item.NativeName,
            ["EnglishName"] =
                item.EnglishName,
            ["Direction"] =
                item.Direction,
            ["Key"] =
                item.Key,
            ["Translation"] =
                item.Translation,
            ["ReviewStatus"] =
                item.RequiresReview
                    ? "Machine translation - review required"
                    : "Reviewed / manual"
        };

    private FileContentResult Workbook(
        IReadOnlyList<Dictionary<string, string>> rows,
        string fileName)
    {
        var columns = new[]
        {
            new ReportExportService.Column(
                "CultureCode",
                "رمز اللغة"),

            new ReportExportService.Column(
                "NativeName",
                "اسم اللغة"),

            new ReportExportService.Column(
                "EnglishName",
                "الاسم بالإنجليزية"),

            new ReportExportService.Column(
                "Direction",
                "الاتجاه"),

            new ReportExportService.Column(
                "Key",
                "النص العربي / المفتاح"),

            new ReportExportService.Column(
                "Translation",
                "الترجمة"),

            new ReportExportService.Column(
                "ReviewStatus",
                "حالة المراجعة")
        };

        var export =
            ReportExportService.Build(
                "xlsx",
                "القاموس",
                columns,
                rows);

        return File(
            export.Bytes,
            export.ContentType,
            $"{fileName}.xlsx");
    }
}