using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;
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

    /// <summary>عضوية أوعية كل الملفات — تُغذّي نماذج السلايد بجافاسكربت.</summary>
    public Dictionary<string, List<string>> BaseMembers { get; set; } = new();

    /// <summary>كتالوج المكوّنات المتاحة للسحب.</summary>
    public static (string Key, string Label)[] BaseComponents => SalaryBaseComposer.Components;

    /// <summary>عضوية ملف بعينه بالرجوع المتدرّج (خاصّته ← العام ← افتراض الكود).</summary>
    public List<string> MembersOf(string baseKey, int profileId) =>
        SalaryBaseStore.Resolve(BaseMembers, baseKey, profileId);

    /// <summary>سياسة ربط الراتب بالحضور المفعَّلة حالياً.</summary>
    public string AttendanceLinkMode { get; set; } = AttendanceSalaryLink.Lenient;

    public async Task OnGetAsync()
    {
        TaxProfiles = await PayrollConfigStore.ListTaxProfilesAsync(_db);
        GosiProfiles = await PayrollConfigStore.ListGosiProfilesAsync(_db);
        BaseMembers = await SalaryBaseStore.AllAsync(_db);
        AttendanceLinkMode = AttendanceSalaryLink.NormalizeMode(
            await HrSettingsStore.GetAsync(_db, AttendanceSalaryLink.ModeKey, AttendanceSalaryLink.Lenient));
    }

    /// <summary>
    /// حفظ سياسة الربط بالحضور. تُغيّر **كل قسيمة قادمة**، فالرسالة تصرّح بالأثر
    /// بدل أن تكتفي بـ«حُفظ».
    /// </summary>
    public async Task<IActionResult> OnPostSaveAttendanceLinkAsync(string mode)
    {
        var resolved = AttendanceSalaryLink.NormalizeMode(mode);
        await HrSettingsStore.SetAsync(_db, AttendanceSalaryLink.ModeKey, resolved);

        TempData["PayrollMessage"] = resolved == AttendanceSalaryLink.Lenient
            ? $"سياسة الربط بالحضور: {AttendanceSalaryLink.ModeLabel(resolved)}."
            : $"سياسة الربط بالحضور: {AttendanceSalaryLink.ModeLabel(resolved)} — ⚠️ من لا بيانات حضور له لن يُحتسب بالمسير القادم.";
        return RedirectToPage();
    }

    /// <summary>
    /// يحفظ عضوية وعاء الملف من نفس نموذج الملف. يُنادى بعد حفظ الملف لأن
    /// الملف الجديد لا يملك معرّفاً قبل ذلك.
    /// وعاء فارغ مسموح عمداً (يعطّل الاقتطاع) لكن الرسالة تُنبّه عليه.
    /// </summary>
    private async Task<string> SaveBaseFromFormAsync(string baseKey, int profileId)
    {
        var components = Request.Form["member"].Where(m => m != null).Select(m => m!).ToList();
        await SalaryBaseStore.SaveMembersAsync(_db, baseKey, profileId, components);

        return components.Count == 0
            ? " ⚠️ الوعاء فارغ — سيصبح الاقتطاع صفراً بالمسير القادم."
            : $" (وعاء الاحتساب: {components.Count} مكوّن)";
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
        var gosiId = await PayrollConfigStore.SaveGosiProfileAsync(_db, profile);
        var gosiNote = await SaveBaseFromFormAsync(SalaryBaseComposer.GosiBaseKey, gosiId);
        TempData["PayrollMessage"] = "تم حفظ ملف الضمان." + gosiNote;
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

        var taxId = await PayrollConfigStore.SaveTaxProfileAsync(_db, profile);
        var taxNote = await SaveBaseFromFormAsync(SalaryBaseComposer.TaxBaseKey, taxId);
        TempData["PayrollMessage"] = "تم حفظ ملف الضريبة وشرائحه." + taxNote;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteTaxAsync(int id)
    {
        await PayrollConfigStore.DeleteTaxProfileAsync(_db, id);
        TempData["PayrollMessage"] = "تم حذف ملف الضريبة.";
        return RedirectToPage();
    }
}
