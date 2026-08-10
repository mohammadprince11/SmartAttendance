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

    /// <summary>كتالوج معايير الشروط — يغذّي محرّر شروط الملف (نفس محرّك الشروط العام).</summary>
    public string CriteriaJson { get; private set; } = "[]";

    /// <summary>سياسة ربط الراتب بالحضور المفعَّلة حالياً.</summary>
    public AttendanceSalaryLink.Policy LinkPolicy { get; set; } = AttendanceSalaryLink.Policy.Default;

    public string AttendanceLinkMode => LinkPolicy.Mode;

    // ── سياسات الأوعية والمقام (كلّها بيانات يحرّرها المستخدم) ──
    /// <summary>وعاء الأوفرتايم: الأساسي وحده أو الأساسي + علاوات مؤهَّلة.</summary>
    public string OvertimeBaseMode { get; set; } = PayrollEarningBase.ModeBasic;
    /// <summary>وعاء الإجازة غير المدفوعة: الأساسي وحده أو الأساسي + علاوات مؤهَّلة.</summary>
    public string UnpaidLeaveBaseMode { get; set; } = PayrollEarningBase.ModeBasic;
    /// <summary>مقام أيام الراتب: ثابت 30 أو أيام الفترة الفعلية.</summary>
    public string SalaryDaysBasis { get; set; } = PayrollDivisorPolicy.BasisFixed30;
    /// <summary>الساعات المعيارية لليوم (مقام الأجر الساعي).</summary>
    public decimal StandardDailyHours { get; set; } = PayrollDivisorPolicy.DefaultDailyHours;

    /// <summary>وعاء الضمان/الضريبة: "Prorated" (القديم) أو "FullBasic" (على الأساسي الكامل).</summary>
    public string GosiTaxBaseMode { get; set; } = "Prorated";

    public async Task OnGetAsync()
    {
        TaxProfiles = await PayrollConfigStore.ListTaxProfilesAsync(_db);
        GosiProfiles = await PayrollConfigStore.ListGosiProfilesAsync(_db);
        BaseMembers = await SalaryBaseStore.AllAsync(_db);
        CriteriaJson = await HrConditionOptions.BuildCatalogJsonAsync(_db);
        LinkPolicy = await AttendanceSalaryLinkSettings.LoadAsync(_db);

        OvertimeBaseMode = PayrollEarningBase.NormalizeMode(
            await HrSettingsStore.GetAsync(_db, "Payroll.OvertimeBaseMode", PayrollEarningBase.ModeBasic));
        UnpaidLeaveBaseMode = PayrollEarningBase.NormalizeMode(
            await HrSettingsStore.GetAsync(_db, "Payroll.UnpaidLeaveBaseMode", PayrollEarningBase.ModeBasic));
        SalaryDaysBasis = PayrollDivisorPolicy.NormalizeBasis(
            await HrSettingsStore.GetAsync(_db, PayrollDivisorPolicy.SalaryDaysBasisKey, PayrollDivisorPolicy.BasisFixed30));
        StandardDailyHours = PayrollDivisorPolicy.DailyHours(
            await HrSettingsStore.GetAsync(_db, PayrollDivisorPolicy.StandardDailyHoursKey, "8"));
        GosiTaxBaseMode = (await HrSettingsStore.GetAsync(_db, "Payroll.GosiTaxBase", "Prorated")) == "FullBasic"
            ? "FullBasic" : "Prorated";
    }

    /// <summary>
    /// حفظ وعاء الضمان/الضريبة: على الأساسي المُنقَّص بالحضور (القديم) أو الأساسي الكامل.
    /// «الكامل» ⟹ لا يتأثر استقطاع الضمان/الضريبة بالغياب؛ الحضور يمسّ الصافي فقط.
    /// </summary>
    public async Task<IActionResult> OnPostSaveGosiTaxBaseAsync(string gosiTaxBase)
    {
        var mode = gosiTaxBase == "FullBasic" ? "FullBasic" : "Prorated";
        await HrSettingsStore.SetAsync(_db, "Payroll.GosiTaxBase", mode);
        TempData["PayrollMessage"] = mode == "FullBasic"
            ? "حُفظ: وعاء الضمان/الضريبة على الأساسي الكامل — يسري على الاحتساب القادم."
            : "حُفظ: وعاء الضمان/الضريبة على الأساسي بعد الحضور (السلوك القديم).";
        return RedirectToPage();
    }

    /// <summary>
    /// حفظ سياسات الأوعية والمقام — كلّها إعداداتٌ تغيّر المسير القادم. الافتراضات
    /// (الأساسي · ثابت 30 · 8 ساعات) تُبقي أرقام اليوم؛ التغيير يُصرَّح أثره بالرسالة.
    /// </summary>
    public async Task<IActionResult> OnPostSaveBasePolicyAsync(
        string overtimeBaseMode, string unpaidLeaveBaseMode, string salaryDaysBasis, string standardDailyHours)
    {
        var otMode = PayrollEarningBase.NormalizeMode(overtimeBaseMode);
        var ulMode = PayrollEarningBase.NormalizeMode(unpaidLeaveBaseMode);
        var basis = PayrollDivisorPolicy.NormalizeBasis(salaryDaysBasis);

        // الساعات تُتحقَّق من مدخلٍ خام قبل التطبيع كي يُرفض «0» أو السالب برسالة
        // بدل أن يُصحَّح صامتاً لـ8، فيعرف المستخدم أن قيمته لم تُقبَل.
        if (decimal.TryParse(standardDailyHours, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var rawHours))
        {
            var hourErrors = PayrollConfigValidation.ValidateStandardDailyHours(rawHours);
            if (hourErrors.Count > 0)
            {
                TempData["PayrollMessage"] = "لم تُحفظ السياسات: " + string.Join(" · ", hourErrors);
                return RedirectToPage();
            }
        }
        var hours = PayrollDivisorPolicy.DailyHours(standardDailyHours);

        await HrSettingsStore.SetAsync(_db, "Payroll.OvertimeBaseMode", otMode);
        await HrSettingsStore.SetAsync(_db, "Payroll.UnpaidLeaveBaseMode", ulMode);
        await HrSettingsStore.SetAsync(_db, PayrollDivisorPolicy.SalaryDaysBasisKey, basis);
        await HrSettingsStore.SetAsync(_db, PayrollDivisorPolicy.StandardDailyHoursKey,
            hours.ToString(System.Globalization.CultureInfo.InvariantCulture));

        string ModeLabel(string m) => m == PayrollEarningBase.ModeBasicPlusAllowances
            ? "الأساسي + علاوات مؤهَّلة" : "الأساسي وحده";
        var basisLabel = basis == PayrollDivisorPolicy.BasisPeriodDays ? "أيام الفترة" : "ثابت 30";

        TempData["PayrollMessage"] =
            $"سياسات الأوعية: أوفرتايم = {ModeLabel(otMode)} · إجازة غير مدفوعة = {ModeLabel(ulMode)} · "
            + $"مقام الأيام = {basisLabel} · ساعات اليوم = {hours:0.##}. تُطبَّق بالمسير القادم.";
        return RedirectToPage();
    }

    /// <summary>
    /// حفظ سياسة الربط بالحضور. تُغيّر **كل قسيمة قادمة**، فالرسالة تصرّح بالأثر
    /// بدل أن تكتفي بـ«حُفظ».
    /// </summary>
    public async Task<IActionResult> OnPostSaveAttendanceLinkAsync(
        string mode, decimal absenceDays, bool allowNegative)
    {
        // المقام لم يعد يُحفظ هنا — يأتي من سياسة الغلق «أيام العمل» بالمسير.
        var policy = new AttendanceSalaryLink.Policy(mode, absenceDays, allowNegative).Normalized();
        await AttendanceSalaryLinkSettings.SaveAsync(_db, policy);

        var notes = new List<string> { AttendanceSalaryLink.ModeLabel(policy.Mode) };
        if (policy.Mode != AttendanceSalaryLink.Lenient)
            notes.Add("⚠️ من لا بيانات حضور له لن يُحتسب بالمسير القادم");
        if (policy.AbsenceDeductionDays != 1m)
            notes.Add($"خصم يوم الغياب = {policy.AbsenceDeductionDays:0.##} يوم");
        if (policy.AllowNegative)
            notes.Add("⚠️ الصافي مسموح أن يكون سالباً");

        TempData["PayrollMessage"] = "سياسة الربط بالحضور: " + string.Join(" · ", notes) + ".";
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

    /// <summary>
    /// شروط الملف تمرّ بالمحرّك ذهاباً وإياباً قبل التخزين: JSON ملفّق من الواجهة
    /// أو حقلٌ بمعيارٍ محذوف يُنظَّف هنا بدل أن يُخزَّن ثم يُتجاهل بصمت وقت الاحتساب.
    /// </summary>
    private static string NormalizeConditions(string? raw) =>
        HrConditions.Serialize(HrConditions.Deserialize(raw));

    /// <summary>سطر يشرح أثر الشروط بالرسالة — الملف المشروط يغيّر اقتطاع من تنطبق عليهم.</summary>
    private static string ConditionNote(string conditionsJson)
    {
        var set = HrConditions.Deserialize(conditionsJson);
        return set.IsEmpty
            ? " (بلا شروط — يُطبَّق بالإسناد اليدوي أو بكونه الملف النشط)"
            : $" (يُطبَّق تلقائياً على: {HrConditions.Describe(set)})";
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
            IsActive = form["IsActive"] == "true",
            ConditionsJson = NormalizeConditions(form["Conditions"]),
            SortOrder = int.TryParse(form["SortOrder"], out var sort) ? sort : 0
        };
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            TempData["PayrollMessage"] = "اسم ملف الضمان مطلوب.";
            return RedirectToPage();
        }
        var gosiErrors = PayrollConfigValidation.ValidateGosi(profile.EmployeeRate, profile.CompanyRate, profile.Ceiling);
        if (gosiErrors.Count > 0)
        {
            TempData["PayrollMessage"] = "لم يُحفظ ملف الضمان: " + string.Join(" · ", gosiErrors);
            return RedirectToPage();
        }
        var gosiId = await PayrollConfigStore.SaveGosiProfileAsync(_db, profile);
        var gosiNote = await SaveBaseFromFormAsync(SalaryBaseComposer.GosiBaseKey, gosiId);
        TempData["PayrollMessage"] = "تم حفظ ملف الضمان." + ConditionNote(profile.ConditionsJson) + gosiNote;
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
            IsActive = form["IsActive"] == "true",
            ConditionsJson = NormalizeConditions(form["Conditions"]),
            SortOrder = int.TryParse(form["SortOrder"], out var sort) ? sort : 0
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

        var taxErrors = PayrollConfigValidation.ValidateTax(
            profile.ExemptionAmount,
            profile.Brackets.Select(b => (b.FromAmount, b.ToAmount, b.Rate)).ToList());
        if (taxErrors.Count > 0)
        {
            TempData["PayrollMessage"] = "لم يُحفظ ملف الضريبة: " + string.Join(" · ", taxErrors);
            return RedirectToPage();
        }

        var taxId = await PayrollConfigStore.SaveTaxProfileAsync(_db, profile);
        var taxNote = await SaveBaseFromFormAsync(SalaryBaseComposer.TaxBaseKey, taxId);
        TempData["PayrollMessage"] = "تم حفظ ملف الضريبة وشرائحه." + ConditionNote(profile.ConditionsJson) + taxNote;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteTaxAsync(int id)
    {
        await PayrollConfigStore.DeleteTaxProfileAsync(_db, id);
        TempData["PayrollMessage"] = "تم حذف ملف الضريبة.";
        return RedirectToPage();
    }
}
