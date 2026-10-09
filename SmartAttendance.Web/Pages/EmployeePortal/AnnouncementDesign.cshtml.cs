using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Announcements.Models;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.EmployeePortal;

[Authorize]
public class AnnouncementDesignModel(ApplicationDbContext db) : PageModel
{
    public async Task<IActionResult> OnGetAsync(int groupId, Guid designId)
    {
        if (!int.TryParse(User.FindFirst("EmployeeId")?.Value, out var employeeId) || employeeId <= 0) return NotFound();
        var tenant = TenantContext.GetTenantId(User);
        var employee = await db.Employees.AsNoTracking().Where(e => e.Id == employeeId && !e.IsDeleted && db.Companies.Any(c => c.Id == e.CompanyId && c.TenantId == tenant))
            .Select(e => new { e.CompanyId }).FirstOrDefaultAsync(HttpContext.RequestAborted);
        if (employee == null) return NotFound();
        var contents = await db.AnnouncementContents.AsNoTracking().Where(c => c.AnnouncementGroupId == groupId && !c.IsDeleted &&
            !c.AnnouncementGroup.IsDeleted && (c.AnnouncementGroup.Status == AnnouncementStatus.Published ||
              (c.AnnouncementGroup.Status == AnnouncementStatus.Expired && c.AnnouncementGroup.ExpirationBehavior == AnnouncementExpirationBehavior.KeepVisibleAsExpired)) &&
            c.AnnouncementGroup.Channels.Any(ch => !ch.IsDeleted && ch.IsEnabled && ch.ChannelType == AnnouncementChannelType.EmployeeWall) &&
            c.AnnouncementGroup.Recipients.Any(r => !r.IsDeleted && r.EmployeeId == employeeId))
            .Select(c => c.PresentationJson).ToListAsync(HttpContext.RequestAborted);
        if (!contents.Any(json => Matches(json, designId))) return NotFound();
        var image = await db.AnnouncementStudioDesigns.AsNoTracking().SingleOrDefaultAsync(d => d.Id == designId && d.CompanyId == employee.CompanyId, HttpContext.RequestAborted);
        if (image == null) return NotFound();
        Response.Headers.CacheControl = "private, no-store"; Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(image.Data, image.ContentType);
    }
    public static bool Matches(string? json, Guid id)
    {
        try { return id != Guid.Empty && json != null && JsonSerializer.Deserialize<StudioPresentation>(json)?.DesignId == id; }
        catch (JsonException) { return false; }
    }
}
