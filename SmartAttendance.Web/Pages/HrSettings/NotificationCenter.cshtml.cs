using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;

namespace SmartAttendance.Web.Pages.HrSettings;

public class NotificationCenterModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public NotificationCenterModel(ApplicationDbContext db)
    {
        _db = db;
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
}
