using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Notifications;

/// <summary>
/// جرس إشعارات بوابة الموظف (الموبايل PWA): يقرأ صندوق <see cref="Domain.Entities.UserNotification"/>
/// الخاص بالموظف الحالي ويعرض بادج «غير مقروء» + منسدلة. نظير جرس الـback-office لكن للموظف،
/// بحالة قراءة لكل مستخدم. يُحقَن بـ_EmployeePortalLayout.
/// </summary>
public sealed class EmployeeNotificationBellViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _db;

    public EmployeeNotificationBellViewComponent(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var claim = ((ClaimsPrincipal)User).FindFirstValue("EmployeeId");
        if (!int.TryParse(claim, out var employeeId) || employeeId <= 0)
        {
            return Content(string.Empty);
        }

        EmployeeNotificationStore.InboxView view;
        try
        {
            view = await EmployeeNotificationStore.GetForEmployeeAsync(_db, employeeId);
        }
        catch
        {
            // الجرس ثانويّ — أي خلل قراءة يجب ألّا يُسقط البوابة. نتراجع بهدوء لعرضٍ فارغ.
            view = EmployeeNotificationStore.Empty;
        }

        return View(view);
    }
}
