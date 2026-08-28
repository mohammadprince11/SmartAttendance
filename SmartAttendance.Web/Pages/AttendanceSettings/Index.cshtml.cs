using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.AttendanceSettings;

/// <summary>
/// إعدادات الحضور (/AttendanceSettings) — المرحلة 2 من مودل الحضور بنمط كيان:
/// قسم دلالات البصمات (تصنيف ثنائي اللغة للبصمات) وقسم مصادر بيانات الحضور
/// (إكسل/عرض قاعدة بيانات/API). راجع قسمي 3.2 و3.3 و15 بدراسة الحضور.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyScopeProvider _companyScope;

    public IndexModel(ApplicationDbContext dbContext, ICompanyScopeProvider companyScope)
    {
        _dbContext = dbContext;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }
    public List<CompanyOption> Companies { get; set; } = new();
    public List<PunchSemanticStore.PunchSemantic> Semantics { get; set; } = new();
    public List<AttendanceSourceStore.AttendanceSource> Sources { get; set; } = new();

    /// <summary>أقل عدد ساعات بين الحضور والانصراف بالبصمة عبر الإنترنت (0 = القاعدة معطّلة).</summary>
    public double MinCheckoutHours { get; set; }
    public bool EnforceGeofence { get; set; }
    public bool RequireBiometric { get; set; }
    public int IdleLogoutMinutes { get; set; }
    public int LeaveLogoutSeconds { get; set; }

    /// <summary>إعادة تحليل اليومية تلقائياً فور الموافقة على إجازة/مغادرة/بصمة مفقودة.</summary>
    public bool AutoReanalyze { get; set; }

    /// <summary>حارس: امنع إعادة التحليل إذا كانت هناك إجراءات منفَّذة على اليوم.</summary>
    public bool GuardExecutedActions { get; set; }

    /// <summary>استثناءا النطاق الجغرافي بحسب اتجاه البصمة (الافتراضي: لا استثناء).</summary>
    public bool AllowOutsideCheckIn { get; set; }
    public bool AllowOutsideCheckOut { get; set; }

    /// <summary>حدود طلبات البصمة المفقودة (0 = بلا حدّ).</summary>
    public int MissingPunchMonthlyLimit { get; set; }
    public int MissingPunchWindowDays { get; set; }
    public bool MissingPunchReasonRequired { get; set; }

    public async Task OnGetAsync()
    {
        var scope = await _companyScope.GetAsync();
        var companies = _dbContext.Companies.AsNoTracking().Where(company => !company.IsDeleted && company.IsActive);
        if (!scope.IsUnrestricted)
        {
            var allowed = scope.AllowedCompanyIds.ToArray();
            companies = companies.Where(company => allowed.Contains(company.Id));
        }
        Companies = await companies.OrderBy(company => company.Name)
            .Select(company => new CompanyOption { Id = company.Id, Name = company.Name }).ToListAsync();
        if (CompanyId is > 0 && !scope.Allows(CompanyId.Value)) CompanyId = null;
        if (CompanyId is null && Companies.Count == 1) CompanyId = Companies[0].Id;

        Semantics = await PunchSemanticStore.ListAsync(_dbContext);
        Sources = await AttendanceSourceStore.ListAsync(_dbContext, scope, CompanyId);
        MinCheckoutHours = await OnlinePunchStore.GetMinCheckoutHoursAsync(_dbContext);
        EnforceGeofence = await OnlinePunchStore.GetEnforceGeofenceAsync(_dbContext);
        RequireBiometric = await OnlinePunchStore.GetRequireBiometricAsync(_dbContext);
        IdleLogoutMinutes = await Web.Infrastructure.Security.PortalSessionPolicy.GetIdleMinutesAsync(_dbContext);
        LeaveLogoutSeconds = await Web.Infrastructure.Security.PortalSessionPolicy.GetLeaveSecondsAsync(_dbContext);
        AutoReanalyze = await AttendanceReanalysisPolicy.GetAutoReanalyzeAsync(_dbContext);
        GuardExecutedActions = await AttendanceReanalysisPolicy.GetGuardExecutedAsync(_dbContext);
        AllowOutsideCheckIn = await OnlinePunchStore.GetAllowOutsideAsync(_dbContext, "In");
        AllowOutsideCheckOut = await OnlinePunchStore.GetAllowOutsideAsync(_dbContext, "Out");
        MissingPunchMonthlyLimit = await MissingPunchPolicy.GetMonthlyLimitAsync(_dbContext);
        MissingPunchWindowDays = await MissingPunchPolicy.GetWindowDaysAsync(_dbContext);
        MissingPunchReasonRequired = await MissingPunchPolicy.GetReasonRequiredAsync(_dbContext);
    }

    /// <summary>حفظ استثناءي النطاق الجغرافي بحسب اتجاه البصمة.</summary>
    public async Task<IActionResult> OnPostSaveGeofenceDirectionAsync()
    {
        await OnlinePunchStore.SetAllowOutsideAsync(
            _dbContext, "In", Request.Form["AllowOutsideCheckIn"] == "true");
        await OnlinePunchStore.SetAllowOutsideAsync(
            _dbContext, "Out", Request.Form["AllowOutsideCheckOut"] == "true");
        TempData["SuccessMessage"] = "حُفظت استثناءات النطاق الجغرافي.";
        return RedirectToPage();
    }

    /// <summary>حفظ حدود طلبات البصمة المفقودة (سقف شهري · نافذة أيام · سبب إلزامي).</summary>
    public async Task<IActionResult> OnPostSaveMissingPunchLimitsAsync()
    {
        await MissingPunchPolicy.SetMonthlyLimitAsync(
            _dbContext, int.TryParse(Request.Form["MissingPunchMonthlyLimit"], out var limit) ? limit : 0);
        await MissingPunchPolicy.SetWindowDaysAsync(
            _dbContext, int.TryParse(Request.Form["MissingPunchWindowDays"], out var days) ? days : 0);
        await MissingPunchPolicy.SetReasonRequiredAsync(
            _dbContext, Request.Form["MissingPunchReasonRequired"] == "true");
        TempData["SuccessMessage"] = "حُفظت حدود طلبات البصمة المفقودة.";
        return RedirectToPage();
    }

    /// <summary>حفظ مفتاحَي إعادة التحليل بعد الموافقات (المفتاح + حارسه المضاد).</summary>
    public async Task<IActionResult> OnPostSaveReanalysisRuleAsync()
    {
        await AttendanceReanalysisPolicy.SetAutoReanalyzeAsync(
            _dbContext, Request.Form["AutoReanalyze"] == "true");
        await AttendanceReanalysisPolicy.SetGuardExecutedAsync(
            _dbContext, Request.Form["GuardExecutedActions"] == "true");
        TempData["SuccessMessage"] = "حُفظت قواعد إعادة التحليل.";
        return RedirectToPage();
    }

    /// <summary>حفظ قواعد جلسة بوابة الموظف (الخمول/مغادرة التطبيق) — حاجز إعارة الهاتف.</summary>
    public async Task<IActionResult> OnPostSaveSessionRuleAsync()
    {
        var idle = int.TryParse(Request.Form["IdleLogoutMinutes"], out var i) && i >= 0 ? i : -1;
        var leave = int.TryParse(Request.Form["LeaveLogoutSeconds"], out var l) && l >= 0 ? l : -1;
        if (idle < 0 || leave < 0)
        {
            TempData["SuccessMessage"] = "أدخل أرقاماً صحيحة (الصفر يعطّل القاعدة).";
            return RedirectToPage();
        }

        await Web.Infrastructure.Security.PortalSessionPolicy.SetIdleMinutesAsync(_dbContext, idle);
        await Web.Infrastructure.Security.PortalSessionPolicy.SetLeaveSecondsAsync(_dbContext, leave);
        TempData["SuccessMessage"] =
            $"تم الحفظ: الخمول = {(idle > 0 ? idle + " دقيقة" : "معطّل")} · مغادرة التطبيق = {(leave > 0 ? leave + " ثانية" : "معطّل")}. يسري خلال دقيقة.";
        return RedirectToPage();
    }

    /// <summary>تفعيل/تعطيل التأكيد البيولوجي (WebAuthn) للبصم الأونلاين.</summary>
    public async Task<IActionResult> OnPostSaveBiometricRuleAsync()
    {
        var enabled = Request.Form["RequireBiometric"] == "true";
        await OnlinePunchStore.SetRequireBiometricAsync(_dbContext, enabled);
        TempData["SuccessMessage"] = enabled
            ? "تم الحفظ: الموظف صاحب مفتاح بصمة/وجه معتمد لا تُقبل بصمته الأونلاين إلا بتأكيد بيولوجي لحظي."
            : "تم الحفظ: التأكيد البيولوجي للبصم معطّل.";
        return RedirectToPage();
    }

    /// <summary>تفعيل/تعطيل إنفاذ النطاق الجغرافي للبصم الأونلاين (للموظفين المسنَدين لمواقع).</summary>
    public async Task<IActionResult> OnPostSaveGeofenceRuleAsync()
    {
        var enabled = Request.Form["EnforceGeofence"] == "true";
        await OnlinePunchStore.SetEnforceGeofenceAsync(_dbContext, enabled);
        TempData["SuccessMessage"] = enabled
            ? "تم الحفظ: البصم الأونلاين للموظفين المسنَدين لمواقع جغرافية لا يُقبل إلا داخل نطاقاتهم."
            : "تم الحفظ: إنفاذ النطاق الجغرافي معطّل.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveOnlinePunchRuleAsync()
    {
        var raw = Request.Form["MinCheckoutHours"].ToString();
        if (!double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var hours)
            && !double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out hours))
        {
            TempData["SuccessMessage"] = "أدخل عدد ساعات صحيحاً.";
            return RedirectToPage();
        }

        await OnlinePunchStore.SetMinCheckoutHoursAsync(_dbContext, hours);
        TempData["SuccessMessage"] = hours > 0
            ? $"تم الحفظ: لا انصراف قبل مرور {OnlinePunchStore.FormatDuration(Math.Clamp(hours, 0, 24))} من الحضور."
            : "تم الحفظ: قاعدة مهلة الانصراف معطّلة.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveSemanticAsync()
    {
        var form = Request.Form;
        var semantic = new PunchSemanticStore.PunchSemantic
        {
            Id = int.TryParse(form["Id"], out var id) ? id : 0,
            Name = form["Name"].ToString().Trim(),
            NameEn = string.IsNullOrWhiteSpace(form["NameEn"]) ? null : form["NameEn"].ToString().Trim(),
            IsActive = form["IsActive"] == "true",
            SortOrder = int.TryParse(form["SortOrder"], out var sort) ? sort : 0,
            IsDeducted = form["IsDeducted"] == "true",
            // نصّ "HH:mm" كما يرسله <input type="time">. الفارغ يبقى null = «بلا نافذة»
            // ⟹ تُخصم الفترة كاملةً، وهو سلوك ما قبل الميزة.
            WindowFrom = string.IsNullOrWhiteSpace(form["WindowFrom"]) ? null : form["WindowFrom"].ToString(),
            WindowTo = string.IsNullOrWhiteSpace(form["WindowTo"]) ? null : form["WindowTo"].ToString()
        };

        if (string.IsNullOrWhiteSpace(semantic.Name))
        {
            TempData["SuccessMessage"] = "اسم الدلالة مطلوب.";
            return RedirectToPage();
        }

        await PunchSemanticStore.SaveAsync(_dbContext, semantic);
        TempData["SuccessMessage"] = semantic.Id > 0 ? "تم تحديث الدلالة." : "تمت إضافة الدلالة.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteSemanticAsync(int id)
    {
        await PunchSemanticStore.DeleteAsync(_dbContext, id);
        TempData["SuccessMessage"] = "تم حذف الدلالة.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveSourceAsync()
    {
        var form = Request.Form;
        var source = new AttendanceSourceStore.AttendanceSource
        {
            Id = int.TryParse(form["Id"], out var id) ? id : 0,
            CompanyId = int.TryParse(form["CompanyId"], out var companyId) ? companyId : null,
            Name = form["Name"].ToString().Trim(),
            ReadType = form["ReadType"].ToString() is { Length: > 0 } type ? type : "Excel",
            ConfigValue = string.IsNullOrWhiteSpace(form["ConfigValue"]) ? null : form["ConfigValue"].ToString().Trim(),
            UsesSemantics = form["UsesSemantics"] == "true",
            IsActive = form["IsActive"] == "true"
        };

        if (string.IsNullOrWhiteSpace(source.Name))
        {
            TempData["SuccessMessage"] = "اسم المصدر مطلوب.";
            return RedirectToPage();
        }

        // حارس خادم: إخفاء الخيار من المنسدلة لا يكفي — النوع بلا منفِّذ يُرفض هنا أيضاً.
        if (!AttendanceSourceStore.IsImplemented(source.ReadType))
        {
            TempData["SuccessMessage"] =
                $"نوع القراءة «{AttendanceSourceStore.ReadTypeLabel(source.ReadType)}» غير منفَّذ بعد.";
            return RedirectToPage();
        }

        var scope = await _companyScope.GetAsync();
        if (source.CompanyId is not > 0 || !scope.Allows(source.CompanyId.Value)) return Forbid();
        await AttendanceSourceStore.SaveAsync(_dbContext, scope, source);
        TempData["SuccessMessage"] = source.Id > 0 ? "تم تحديث المصدر." : "تمت إضافة المصدر.";
        return RedirectToPage(new { CompanyId = source.CompanyId });
    }

    public async Task<IActionResult> OnPostDeleteSourceAsync(int id, int companyId)
    {
        var scope = await _companyScope.GetAsync();
        if (!scope.Allows(companyId)) return Forbid();
        await AttendanceSourceStore.DeleteAsync(_dbContext, scope, companyId, id);
        TempData["SuccessMessage"] = "تم حذف المصدر.";
        return RedirectToPage(new { CompanyId = companyId });
    }

    public sealed class CompanyOption
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
    }
}
