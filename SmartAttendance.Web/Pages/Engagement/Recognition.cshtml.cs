using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Application.Announcements.Services;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Engagement;

public class RecognitionModel : EngagementPageModel
{
    public RecognitionModel(
        ApplicationDbContext dbContext,
        IAnnouncementService announcementService)
        : base(dbContext, announcementService)
    {
    }

    /// <summary>
    /// الشاشة صارت تبويباً بـ<c>/Engagement</c>. يبقى المسار حيّاً لروابطٍ قديمة
    /// أو مفضّلات، ويعيد التوجيه بدل أن يعرض نسخةً ثانية من الشاشة نفسها.
    /// </summary>
    public IActionResult OnGet() => RedirectToPage("/Engagement/Index", new { tab = "recognition" });
}
