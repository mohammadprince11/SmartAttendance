using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.AttendanceSettings;

/// <summary>
/// إعدادات الحضور (/AttendanceSettings) — المرحلة 2 من مودل الحضور بنمط كيان:
/// قسم دلالات البصمات (تصنيف ثنائي اللغة للبصمات) وقسم مصادر بيانات الحضور
/// (إكسل/عرض قاعدة بيانات/API). راجع قسمي 3.2 و3.3 و15 بدراسة الحضور.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

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

    public async Task OnGetAsync()
    {
        Semantics = await PunchSemanticStore.ListAsync(_dbContext);
        Sources = await AttendanceSourceStore.ListAsync(_dbContext);
        MinCheckoutHours = await OnlinePunchStore.GetMinCheckoutHoursAsync(_dbContext);
        EnforceGeofence = await OnlinePunchStore.GetEnforceGeofenceAsync(_dbContext);
        RequireBiometric = await OnlinePunchStore.GetRequireBiometricAsync(_dbContext);
        IdleLogoutMinutes = await Web.Infrastructure.Security.PortalSessionPolicy.GetIdleMinutesAsync(_dbContext);
        LeaveLogoutSeconds = await Web.Infrastructure.Security.PortalSessionPolicy.GetLeaveSecondsAsync(_dbContext);
        AutoReanalyze = await AttendanceReanalysisPolicy.GetAutoReanalyzeAsync(_dbContext);
        GuardExecutedActions = await AttendanceReanalysisPolicy.GetGuardExecutedAsync(_dbContext);
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
            SortOrder = int.TryParse(form["SortOrder"], out var sort) ? sort : 0
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

        await AttendanceSourceStore.SaveAsync(_dbContext, source);
        TempData["SuccessMessage"] = source.Id > 0 ? "تم تحديث المصدر." : "تمت إضافة المصدر.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteSourceAsync(int id)
    {
        await AttendanceSourceStore.DeleteAsync(_dbContext, id);
        TempData["SuccessMessage"] = "تم حذف المصدر.";
        return RedirectToPage();
    }
}
