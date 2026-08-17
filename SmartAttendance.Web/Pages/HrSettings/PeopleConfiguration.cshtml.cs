using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.HrSettings;

/// <summary>
/// تهيئة الأشخاص (نظير `/Employees/Configuration` بكيان).
///
/// أول قاعدة هنا: اشتقاق المواطنة من الجنسية — المنطق بـ<see cref="CitizenshipPolicy"/>
/// وهذه الصفحة تهيئة وعرضُ أثرٍ فقط.
/// </summary>
public class PeopleConfigurationModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public PeopleConfigurationModel(ApplicationDbContext db) => _db = db;

    [BindProperty] public bool CitizenshipEnabled { get; set; }
    [BindProperty] public string CitizenNationalities { get; set; } = CitizenshipPolicy.DefaultNationalities;

    /// <summary>القائمة بعد الفكّ — تُعرض كي يرى المستخدم ما سيُطبَّق فعلاً.</summary>
    public IReadOnlyList<string> ParsedNationalities { get; private set; } = Array.Empty<string>();

    public async Task OnGetAsync()
    {
        await HrSettingsStore.EnsureTablesAsync(_db);

        CitizenshipEnabled = bool.TryParse(
            await HrSettingsStore.GetAsync(_db, CitizenshipPolicy.KeyEnabled, "False"), out var enabled) && enabled;
        CitizenNationalities = await HrSettingsStore.GetAsync(
            _db, CitizenshipPolicy.KeyNationalities, CitizenshipPolicy.DefaultNationalities);
        ParsedNationalities = CitizenshipPolicy.Parse(CitizenNationalities);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await HrSettingsStore.EnsureTablesAsync(_db);

        var cleaned = string.Join("، ", CitizenshipPolicy.Parse(CitizenNationalities));

        // قاعدة مفعّلة بقائمة فارغة = كل الموظفين «غير مواطن» بصمت — نرفض المدخل
        // الفاسد بدل حفظ سلوكٍ خاطئ (نفس مبدأ حرّاس التهيئة بالرواتب).
        if (CitizenshipEnabled && cleaned.Length == 0)
        {
            ModelState.AddModelError(nameof(CitizenNationalities),
                "فعّلتَ القاعدة بلا أي جنسية — أضف جنسية واحدة على الأقل أو عطّل القاعدة.");
            ParsedNationalities = Array.Empty<string>();
            return Page();
        }

        await HrSettingsStore.SetAsync(_db, CitizenshipPolicy.KeyEnabled, CitizenshipEnabled.ToString());
        await HrSettingsStore.SetAsync(_db, CitizenshipPolicy.KeyNationalities,
            cleaned.Length == 0 ? CitizenshipPolicy.DefaultNationalities : cleaned);

        TempData["SuccessMessage"] = CitizenshipEnabled
            ? $"تم الحفظ. الموظف الجديد يُوسم مواطناً إذا كانت جنسيته: {cleaned}."
            : "تم الحفظ. القاعدة معطّلة — حقل «مواطن» يبقى بيد المدخِل.";

        return RedirectToPage();
    }
}
