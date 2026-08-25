using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;
using SmartAttendance.Web.Infrastructure.Notifications;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.HrSettings;

[Authorize(Roles = RoleRouteCatalog.Admin)]
public class NotificationCenterModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IWebPushSender _webPush;

    public NotificationCenterModel(ApplicationDbContext db, IWebPushSender webPush)
    {
        _db = db;
        _webPush = webPush;
    }

    public List<NotificationRuleRow> Rules { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Rules = await HrSettingsStore.LoadNotificationRulesAsync(_db);
    }

    public async Task<IActionResult> OnPostToggleRuleAsync(int id)
    {
        Rules = await HrSettingsStore.LoadNotificationRulesAsync(_db);
        var rule = Rules.FirstOrDefault(x => x.Id == id);
        if (rule != null)
        {
            await HrSettingsStore.ToggleNotificationRuleAsync(_db, id, !rule.IsEnabled);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateRuleAsync(int id, string audience, int daysBefore, string selectedItems, string supervisorName)
    {
        await HrSettingsStore.UpdateNotificationRuleAsync(_db, id, audience, daysBefore, selectedItems, supervisorName);
        TempData["SuccessMessage"] = "تم تحديث إعدادات الإشعار.";
        return RedirectToPage();
    }

    /// <summary>
    /// تشغيل مولّد مركز الإشعارات يدوياً الآن (بدل انتظار الكرون اليومي) — للتحقق والتشغيل
    /// عند الطلب. idempotent: يُطلق الأحداث الجديدة فقط (منع تكرار بجدول الأحداث).
    /// </summary>
    public async Task<IActionResult> OnPostGenerateNowAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(GetBaghdadNow().DateTime);
        var result = await NotificationRuleGenerator.GenerateAsync(_db, _webPush, today, cancellationToken);

        TempData["SuccessMessage"] = result.NewEvents == 0
            ? "لا أحداث جديدة اليوم (كل الأحداث المستحقّة أُطلقت سابقاً أو لا قواعد زمنية مفعّلة)."
            : $"تم التوليد: {result.NewEvents} حدث جديد، {result.NotificationsCreated} إشعار داخل النظام، {result.PushDelivered} دفعة Web-Push.";

        return RedirectToPage();
    }

    private static DateTimeOffset GetBaghdadNow()
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById("Arabic Standard Time"); }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baghdad"); }
        return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
    }
}
