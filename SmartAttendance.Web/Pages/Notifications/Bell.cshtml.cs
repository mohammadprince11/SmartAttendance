using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Notifications;

namespace SmartAttendance.Web.Pages.Notifications;

/// <summary>
/// نقاط نهاية JSON لجرس الـback-office: تعليم إشعار مفرد أو الكل كمقروء ضمن نطاق دور
/// المستخدم الحالي (الأمان في <see cref="BackOfficeNotificationStore"/> عبر النطاق نفسه).
/// </summary>
public class BellModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public BellModel(ApplicationDbContext db)
    {
        _db = db;
    }

    private BackOfficeNotificationScope.Scope ResolveScope() =>
        BackOfficeNotificationScope.Resolve(
            User.FindFirstValue(ClaimTypes.Role),
            User.Identity?.Name);

    public async Task<IActionResult> OnPostReadAsync(int id)
    {
        var scope = ResolveScope();
        var affected = await BackOfficeNotificationStore.MarkReadAsync(_db, scope, id);
        var view = await BackOfficeNotificationStore.GetForUserAsync(_db, scope);
        return new JsonResult(new { ok = affected > 0, unread = view.UnreadCount });
    }

    public async Task<IActionResult> OnPostReadAllAsync()
    {
        var scope = ResolveScope();
        var affected = await BackOfficeNotificationStore.MarkAllReadAsync(_db, scope);
        return new JsonResult(new { ok = affected > 0, unread = 0 });
    }
}
