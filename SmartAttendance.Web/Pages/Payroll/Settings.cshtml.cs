using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// تهيئة الضريبة والضمان (/Payroll/Settings) — ملفات ضريبة بشرائح تصاعدية + ملفات
/// ضمان (نسبة موظف/شركة + سقف). المسير يستخدم الملف النشط. القيم مبدئية عراقية
/// تحتاج تأكيد محاسب.
/// </summary>
public class SettingsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public SettingsModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public List<PayrollConfigStore.TaxProfile> TaxProfiles { get; set; } = new();
    public List<PayrollConfigStore.GosiProfile> GosiProfiles { get; set; } = new();

    /// <summary>عضوية وعاء الضريبة — أي مكوّنات القسيمة تدخل باحتسابها.</summary>
    public List<string> TaxMembers { get; set; } = new();

    /// <summary>عضوية وعاء الضمان.</summary>
    public List<string> GosiMembers { get; set; } = new();

    /// <summary>المكوّنات غير المنضمّة لأي وعاء — مصدر السحب.</summary>
    public List<(string Key, string Label)> Unassigned(List<string> members) =>
        SalaryBaseComposer.Components.Where(c => !members.Contains(c.Key)).ToList();

    public static string ComponentLabel(string key) =>
        SalaryBaseComposer.Components.FirstOrDefault(c => c.Key == key).Label ?? key;

    public async Task OnGetAsync()
    {
        TaxProfiles = await PayrollConfigStore.ListTaxProfilesAsync(_db);
        GosiProfiles = await PayrollConfigStore.ListGosiProfilesAsync(_db);
        (TaxMembers, GosiMembers) = await SalaryBaseStore.BothAsync(_db);
    }

    /// <summary>
    /// حفظ عضوية وعاء واحد. الترتيب المرسل هو ترتيب الإفلات بالواجهة.
    /// وعاء فارغ مسموح عمداً (يعطّل الاقتطاع) لكن يُنبَّه عليه بوضوح لأن أثره
    /// يظهر على كل قسيمة.
    /// </summary>
    public async Task<IActionResult> OnPostSaveBaseAsync()
    {
        var baseKey = Request.Form["BaseKey"].ToString().Trim();
        if (baseKey != SalaryBaseComposer.TaxBaseKey && baseKey != SalaryBaseComposer.GosiBaseKey)
        {
            TempData["PayrollMessage"] = "وعاء غير معروف.";
            return RedirectToPage();
        }

        var components = Request.Form["member"].Where(m => m != null).Select(m => m!).ToList();
        await SalaryBaseStore.SaveMembersAsync(_db, baseKey, components);

        var name = baseKey == SalaryBaseComposer.GosiBaseKey ? "الضمان" : "الضريبة";
        TempData["PayrollMessage"] = components.Count == 0
            ? $"تم حفظ وعاء {name} فارغاً — سيصبح الاقتطاع صفراً بالمسير القادم."
            : $"تم حفظ وعاء {name} ({components.Count} مكوّن).";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveGosiAsync()
    {
        var form = Request.Form;
        var profile = new PayrollConfigStore.GosiProfile
        {
            Id = int.TryParse(form["Id"], out var id) ? id : 0,
            Name = form["Name"].ToString().Trim(),
            EmployeeRate = decimal.TryParse(form["EmployeeRate"], out var er) ? er : 0,
            CompanyRate = decimal.TryParse(form["CompanyRate"], out var cr) ? cr : 0,
            Ceiling = decimal.TryParse(form["Ceiling"], out var c) ? c : 0,
            IsActive = form["IsActive"] == "true"
        };
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            TempData["PayrollMessage"] = "اسم ملف الضمان مطلوب.";
            return RedirectToPage();
        }
        await PayrollConfigStore.SaveGosiProfileAsync(_db, profile);
        TempData["PayrollMessage"] = "تم حفظ ملف الضمان.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteGosiAsync(int id)
    {
        await PayrollConfigStore.DeleteGosiProfileAsync(_db, id);
        TempData["PayrollMessage"] = "تم حذف ملف الضمان.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveTaxAsync()
    {
        var form = Request.Form;
        var profile = new PayrollConfigStore.TaxProfile
        {
            Id = int.TryParse(form["Id"], out var id) ? id : 0,
            Name = form["Name"].ToString().Trim(),
            ExemptionAmount = decimal.TryParse(form["ExemptionAmount"], out var ex) ? ex : 0,
            IsActive = form["IsActive"] == "true"
        };
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            TempData["PayrollMessage"] = "اسم ملف الضريبة مطلوب.";
            return RedirectToPage();
        }

        var froms = form["bracket_from"];
        var tos = form["bracket_to"];
        var rates = form["bracket_rate"];
        for (var i = 0; i < froms.Count; i++)
        {
            if (!decimal.TryParse(froms[i], out var from)) continue;
            if (!decimal.TryParse(rates[i], out var rate)) continue;
            decimal? to = decimal.TryParse(tos[i], out var t) && t > 0 ? t : null;
            profile.Brackets.Add(new PayrollConfigStore.TaxBracket { FromAmount = from, ToAmount = to, Rate = rate });
        }

        await PayrollConfigStore.SaveTaxProfileAsync(_db, profile);
        TempData["PayrollMessage"] = "تم حفظ ملف الضريبة وشرائحه.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteTaxAsync(int id)
    {
        await PayrollConfigStore.DeleteTaxProfileAsync(_db, id);
        TempData["PayrollMessage"] = "تم حذف ملف الضريبة.";
        return RedirectToPage();
    }
}
