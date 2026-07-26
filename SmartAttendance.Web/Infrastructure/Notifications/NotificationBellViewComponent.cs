using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>
/// جرس إشعارات الـback-office في الشريط العلوي: يقرأ <c>SystemNotifications</c> ضمن نطاق
/// دور المستخدم الحالي ويعرض بادج «غير مقروء» + قائمة منسدلة بآخر الإشعارات. يُحلّ الفجوة
/// الجذرية: الجدول كان يُكتَب (موافقات/مخالفات/طلبات مالية/مولّد القواعد) ولا يُقرأ أبداً.
/// </summary>
public sealed class NotificationBellViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _db;

    public NotificationBellViewComponent(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var role = ((ClaimsPrincipal)User).FindFirstValue(ClaimTypes.Role);
        var username = ((ClaimsPrincipal)User).Identity?.Name;

        var scope = BackOfficeNotificationScope.Resolve(role, username);
        if (scope.IsEmpty)
        {
            return Content(string.Empty);
        }

        BackOfficeNotificationStore.BellView view;
        try
        {
            view = await BackOfficeNotificationStore.GetForUserAsync(_db, scope);
        }
        catch
        {
            // الجرس ثانويّ للشريط العلوي: أي خلل قراءة (جدول غير مهيّأ بعدُ مثلاً) يجب ألّا
            // يُسقط الصفحة كلها — نتراجع بهدوء لعرضٍ فارغ.
            view = BackOfficeNotificationStore.Empty;
        }

        return View(view);
    }
}
