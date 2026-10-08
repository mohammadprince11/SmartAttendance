using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Account;

[Authorize]
public sealed class FarewellModel(ApplicationDbContext db) : PageModel
{
    public IReadOnlyList<string> Messages { get; private set; } = [];
    public DateTimeOffset AccessEndsAtUtc { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Response.Headers.CacheControl = "no-store";
        var tenant = TenantContext.GetTenantId(User) ?? 0;
        var username = User.Identity?.Name;
        var schedule = await EndServiceAccessStore.GetAsync(db, tenant, username);
        if (schedule is null) return RedirectToPage("/EmployeePortal/Index");
        var account = await LoginDatabase.GetByUsernameAsync(db, tenant, username ?? "");
        if (account?.IsActive != true) return Unauthorized();
        AccessEndsAtUtc = schedule.AccessEndsAtUtc;
        // Only this service period's own farewell, never a general/private inbox.
        var target = EndServiceAccessStore.FarewellPath + "?serviceId=" + schedule.EndServiceId;
        Messages = await db.Set<UserNotificationRecipient>().AsNoTracking()
            .Where(r => r.EmployeeId == schedule.EmployeeId && r.Employee.CompanyId == schedule.CompanyId
                && r.UserNotification.Url == target)
            .OrderByDescending(r => r.Id).Take(5)
            .Select(r => r.UserNotification.MessageAr ?? "").ToListAsync(HttpContext.RequestAborted);
        return Page();
    }
}
