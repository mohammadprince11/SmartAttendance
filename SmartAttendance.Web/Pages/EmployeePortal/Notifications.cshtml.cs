using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Notifications;

namespace SmartAttendance.Web.Pages.EmployeePortal;

/// <summary>
/// نقاط نهاية JSON لجرس بوابة الموظف: تعليم إشعار مفرد أو الكل كمقروء لصندوق الموظف
/// الحالي (UserNotificationRecipient.IsRead). محصور بموظف الـclaim فلا يعلّم أحدٌ صندوق غيره.
/// </summary>
public class NotificationsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public NotificationsModel(ApplicationDbContext db)
    {
        _db = db;
    }

    private int EmployeeId =>
        int.TryParse(User.FindFirstValue("EmployeeId"), out var id) && id > 0 ? id : 0;

    /// <summary>لا عرض مرئي هنا — فتح الرابط يدوياً يعود للبوابة بدل صفحة فارغة بقوقعة الباك-أوفيس.</summary>
    public IActionResult OnGet() => RedirectToPage("/EmployeePortal/Index");

    public async Task<IActionResult> OnPostReadAsync(int id)
    {
        var affected = await EmployeeNotificationStore.MarkReadAsync(_db, EmployeeId, id, HttpContext.RequestAborted);
        var view = await EmployeeNotificationStore.GetForEmployeeAsync(_db, EmployeeId, ct: HttpContext.RequestAborted);
        return new JsonResult(new { ok = affected > 0, unread = view.UnreadCount });
    }

    public async Task<IActionResult> OnPostReadAllAsync()
    {
        var affected = await EmployeeNotificationStore.MarkAllReadAsync(_db, EmployeeId, HttpContext.RequestAborted);
        return new JsonResult(new { ok = affected > 0, unread = 0 });
    }
}
