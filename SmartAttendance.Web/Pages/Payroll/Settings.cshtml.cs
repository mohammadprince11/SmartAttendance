using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// تهيئة الضريبة والضمان (/Payroll/Settings) — ملفات ضريبة بشرائح تصاعدية + ملفات
/// ضمان (نسبة موظف/شركة + سقف). المسير يستخدم الملف النشط. القيم مبدئية عراقية
/// تحتاج تأكيد محاسب.
/// </summary>
public class SettingsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;

    public SettingsModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }
    public List<CompanyOption> Companies { get; set; } = new();
    public sealed class CompanyOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
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
        var scope = await _companyScope.GetAsync();
        await LoadCompaniesAsync(scope);
        if (CompanyId.HasValue && !scope.Allows(CompanyId.Value))
        {
            TaxProfiles = new();
            GosiProfiles = new();
            return;
        }
        TaxProfiles = await PayrollConfigStore.ListTaxProfilesAsync(_db, scope, CompanyId);
        GosiProfiles = await PayrollConfigStore.ListGosiProfilesAsync(_db, scope, CompanyId);
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

        RequireCommitteeApproval = bool.TryParse(
            await HrSettingsStore.GetAsync(_db, PayrollRunStore.KeyRequireCommitteeApproval, "False"), out var rca) && rca;

        FiscalYearStartMonth = int.TryParse(await HrSettingsStore.GetAsync(_db, KeyFiscalYearStartMonth, "1"), out var fy) && fy is >= 1 and <= 12 ? fy : 1;
        ExtraSalariesPerYear = int.TryParse(await HrSettingsStore.GetAsync(_db, KeyExtraSalariesPerYear, "0"), out var es) && es >= 0 ? es : 0;

        ConfigMonitorEnabled = bool.TryParse(
            await HrSettingsStore.GetAsync(_db, PayrollConfigChangeMonitor.KeyEnabled, "False"), out var cme) && cme;
        ConfigMonitorRole = await HrSettingsStore.GetAsync(_db, PayrollConfigChangeMonitor.KeyTargetRole, PayrollConfigChangeMonitor.DefaultTargetRole);

        Caps = PayrollCapsPolicy.Parse(
            await HrSettingsStore.GetAsync(_db, PayrollCapsPolicy.KeyDeductionCapAmount, "0"),
            await HrSettingsStore.GetAsync(_db, PayrollCapsPolicy.KeyDeductionCapPercent, "0"),
            await HrSettingsStore.GetAsync(_db, PayrollCapsPolicy.KeyOvertimeCapAmount, "0"),
            await HrSettingsStore.GetAsync(_db, PayrollCapsPolicy.KeyOvertimeCapHours, "0"));
    }

    /// <summary>
    /// كل كتابة إعداد رواتب من هذه الشاشة تمرّ من مراقب التغيير: تسجيل قبل/بعد بالتدقيق
    /// دائماً، وإشعار للجهة المستهدفة إن فُعّل (نظير «الخيارات الأمنية» بكيان).
    /// </summary>
    private Task<bool> TrackAsync(string key, string? value) =>
        PayrollConfigChangeMonitor.SetAndTrackAsync(
            _db, key, value, User?.Identity?.Name ?? "system", HttpContext.Connection.RemoteIpAddress?.ToString());

    // ── السنة المالية (نظير «السنة المالية» بكيان: بداية السنة · عدد الرواتب الإضافية · ساعات الدوام) ──
    public const string KeyFiscalYearStartMonth = "Payroll.FiscalYear.StartMonth";
    public const string KeyExtraSalariesPerYear = "Payroll.FiscalYear.ExtraSalaries";

    /// <summary>شهر بداية السنة المالية (1–12) — يحكم الاحتساب التراكمي/التقارير السنوية.</summary>
    public int FiscalYearStartMonth { get; set; } = 1;

    /// <summary>
    /// عدد الرواتب الإضافية بالسنة (نظير كيان «2»). تهيئةٌ توثيقية الآن: مسير الرواتب
    /// الإضافية كمسير موازٍ يمسّ محرّك الاحتساب فيحتاج طلباً صريحاً (قاعدة الرواتب الحمراء).
    /// </summary>
    public int ExtraSalariesPerYear { get; set; }

    public async Task<IActionResult> OnPostSaveFiscalYearAsync(int fiscalYearStartMonth, int extraSalariesPerYear)
    {
        if (fiscalYearStartMonth is < 1 or > 12)
        {
            TempData["PayrollMessage"] = "شهر بداية السنة المالية بين 1 و12."; TempData["PayrollOk"] = false;
            return RedirectToPage();
        }
        if (extraSalariesPerYear is < 0 or > 12)
        {
            TempData["PayrollMessage"] = "عدد الرواتب الإضافية بين 0 و12."; TempData["PayrollOk"] = false;
            return RedirectToPage();
        }

        await TrackAsync(KeyFiscalYearStartMonth, fiscalYearStartMonth.ToString());
        await TrackAsync(KeyExtraSalariesPerYear, extraSalariesPerYear.ToString());
        TempData["PayrollMessage"] = "حُفظت إعدادات السنة المالية.";
        return RedirectToPage();
    }

    /// <summary>حالة مراقبة تغيير الإعدادات (إشعار مفعَّل؟ + الجهة).</summary>
    public bool ConfigMonitorEnabled { get; set; }
    public string ConfigMonitorRole { get; set; } = PayrollConfigChangeMonitor.DefaultTargetRole;

    public async Task<IActionResult> OnPostSaveMonitorAsync(bool monitorEnabled, string? monitorRole)
    {
        await TrackAsync(PayrollConfigChangeMonitor.KeyEnabled, monitorEnabled.ToString());
        await TrackAsync(PayrollConfigChangeMonitor.KeyTargetRole,
            string.IsNullOrWhiteSpace(monitorRole) ? PayrollConfigChangeMonitor.DefaultTargetRole : monitorRole.Trim());
        TempData["PayrollMessage"] = monitorEnabled
            ? "حُفظ: كل تغيير بإعدادات الرواتب يُسجَّل بالتدقيق ويُشعَر به الدور المستهدف فوراً."
            : "حُفظ: التغييرات تُسجَّل بالتدقيق فقط بلا إشعار.";
        return RedirectToPage();
    }

    /// <summary>الحدود القصوى الشهرية الحالية (نظير «التحقق من المبالغ القصوى» بكيان).</summary>
    public PayrollCapsPolicy.Caps Caps { get; set; } = PayrollCapsPolicy.Caps.None;

    /// <summary>هل يتطلب إصدار الرواتب اعتماد لجنة؟ (نظير «خيارات الموافقات» بتهيئة كيان).</summary>
    public bool RequireCommitteeApproval { get; set; }

    public async Task<IActionResult> OnPostSaveApprovalAsync(bool requireCommitteeApproval)
    {
        await TrackAsync(PayrollRunStore.KeyRequireCommitteeApproval, requireCommitteeApproval.ToString());
        TempData["PayrollMessage"] = requireCommitteeApproval
            ? "حُفظ: إصدار الرواتب يتطلب اعتماد اللجنة على الدفعة المقفلة أولاً."
            : "حُفظ: الإصدار بلا اشتراط اعتماد لجنة (السلوك القائم).";
        return RedirectToPage();
    }

    /// <summary>
    /// حفظ الحدود القصوى: صفر أو فارغ = بلا سقف (السلوك القائم). النسبة تُحصر 0–100.
    /// السقف يمسّ الاقتطاعات الاختيارية والإضافي فقط — لا الضريبة ولا الضمان.
    /// </summary>
    public async Task<IActionResult> OnPostSaveCapsAsync(
        string? deductionCapAmount, string? deductionCapPercent, string? overtimeCapAmount, string? overtimeCapHours)
    {
        var caps = PayrollCapsPolicy.Parse(deductionCapAmount, deductionCapPercent, overtimeCapAmount, overtimeCapHours);
        if (caps.DeductionCapPercentOfGross > 100)
        {
            TempData["PayrollMessage"] = "نسبة سقف الاقتطاع لا تتجاوز 100% من الإجمالي.";
            TempData["PayrollOk"] = false;
            return RedirectToPage();
        }

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        await TrackAsync(PayrollCapsPolicy.KeyDeductionCapAmount, caps.DeductionCapAmount.ToString(inv));
        await TrackAsync(PayrollCapsPolicy.KeyDeductionCapPercent, caps.DeductionCapPercentOfGross.ToString(inv));
        await TrackAsync(PayrollCapsPolicy.KeyOvertimeCapAmount, caps.OvertimeCapAmount.ToString(inv));
        await TrackAsync(PayrollCapsPolicy.KeyOvertimeCapHours, caps.OvertimeCapHours.ToString(inv));

        TempData["PayrollMessage"] = caps.HasDeductionCap || caps.HasOvertimeCap
            ? "حُفظت الحدود القصوى — تسري على الاحتساب القادم، والمتجاوز يُعلَن بسطر بالقسيمة."
            : "حُفظ: بلا حدود قصوى (السلوك القائم).";
        return RedirectToPage();
    }

    /// <summary>
    /// حفظ وعاء الضمان/الضريبة: على الأساسي المُنقَّص بالحضور (القديم) أو الأساسي الكامل.
    /// «الكامل» ⟹ لا يتأثر استقطاع الضمان/الضريبة بالغياب؛ الحضور يمسّ الصافي فقط.
    /// </summary>
    public async Task<IActionResult> OnPostSaveGosiTaxBaseAsync(string gosiTaxBase)
    {
        var mode = gosiTaxBase == "FullBasic" ? "FullBasic" : "Prorated";
        await TrackAsync("Payroll.GosiTaxBase", mode);
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

        await TrackAsync("Payroll.OvertimeBaseMode", otMode);
        await TrackAsync("Payroll.UnpaidLeaveBaseMode", ulMode);
        await TrackAsync(PayrollDivisorPolicy.SalaryDaysBasisKey, basis);
        await TrackAsync(PayrollDivisorPolicy.StandardDailyHoursKey,
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
        var scope = await _companyScope.GetAsync();
        if (CompanyId is not > 0 || !scope.Allows(CompanyId)) return Forbid();
        var form = Request.Form;
        var profile = new PayrollConfigStore.GosiProfile
        {
            Id = int.TryParse(form["Id"], out var id) ? id : 0,
            CompanyId = CompanyId,
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
            return RedirectToPage(new { CompanyId });
        }
        var gosiErrors = PayrollConfigValidation.ValidateGosi(profile.EmployeeRate, profile.CompanyRate, profile.Ceiling);
        if (gosiErrors.Count > 0)
        {
            TempData["PayrollMessage"] = "لم يُحفظ ملف الضمان: " + string.Join(" · ", gosiErrors);
            return RedirectToPage(new { CompanyId });
        }
        var gosiId = await PayrollConfigStore.SaveGosiProfileAsync(_db, scope, profile);
        var gosiNote = await SaveBaseFromFormAsync(SalaryBaseComposer.GosiBaseKey, gosiId);
        TempData["PayrollMessage"] = "تم حفظ ملف الضمان." + ConditionNote(profile.ConditionsJson) + gosiNote;
        return RedirectToPage(new { CompanyId });
    }

    public async Task<IActionResult> OnPostDeleteGosiAsync(int id)
    {
        var scope = await _companyScope.GetAsync();
        if (CompanyId is not > 0 || !scope.Allows(CompanyId)) return Forbid();
        await PayrollConfigStore.DeleteGosiProfileAsync(_db, scope, id);
        TempData["PayrollMessage"] = "تم حذف ملف الضمان.";
        return RedirectToPage(new { CompanyId });
    }

    public async Task<IActionResult> OnPostSaveTaxAsync()
    {
        var scope = await _companyScope.GetAsync();
        if (CompanyId is not > 0 || !scope.Allows(CompanyId)) return Forbid();
        var form = Request.Form;
        var profile = new PayrollConfigStore.TaxProfile
        {
            Id = int.TryParse(form["Id"], out var id) ? id : 0,
            CompanyId = CompanyId,
            Name = form["Name"].ToString().Trim(),
            ExemptionAmount = decimal.TryParse(form["ExemptionAmount"], out var ex) ? ex : 0,
            IsActive = form["IsActive"] == "true",
            ConditionsJson = NormalizeConditions(form["Conditions"]),
            SortOrder = int.TryParse(form["SortOrder"], out var sort) ? sort : 0
        };
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            TempData["PayrollMessage"] = "اسم ملف الضريبة مطلوب.";
            return RedirectToPage(new { CompanyId });
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
            return RedirectToPage(new { CompanyId });
        }

        var taxId = await PayrollConfigStore.SaveTaxProfileAsync(_db, scope, profile);
        var taxNote = await SaveBaseFromFormAsync(SalaryBaseComposer.TaxBaseKey, taxId);
        TempData["PayrollMessage"] = "تم حفظ ملف الضريبة وشرائحه." + ConditionNote(profile.ConditionsJson) + taxNote;
        return RedirectToPage(new { CompanyId });
    }

    public async Task<IActionResult> OnPostDeleteTaxAsync(int id)
    {
        var scope = await _companyScope.GetAsync();
        if (CompanyId is not > 0 || !scope.Allows(CompanyId)) return Forbid();
        await PayrollConfigStore.DeleteTaxProfileAsync(_db, scope, id);
        TempData["PayrollMessage"] = "تم حذف ملف الضريبة.";
        return RedirectToPage(new { CompanyId });
    }

    private async Task LoadCompaniesAsync(CompanyScope scope)
    {
        var query = _db.Companies.AsNoTracking().Where(company => !company.IsDeleted && company.IsActive);
        if (!scope.IsUnrestricted)
        {
            var allowed = scope.AllowedCompanyIds.ToArray();
            query = query.Where(company => allowed.Contains(company.Id));
        }

        Companies = await query.OrderBy(company => company.Name)
            .Select(company => new CompanyOption { Id = company.Id, Name = company.Name })
            .ToListAsync();
        if (!CompanyId.HasValue && Companies.Count == 1) CompanyId = Companies[0].Id;
    }
}
