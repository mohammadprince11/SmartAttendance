using SmartAttendance.Application.Announcements.Services;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Engagement;

public class IndexModel : EngagementPageModel
{
    public IndexModel(
        ApplicationDbContext dbContext,
        IAnnouncementService announcementService)
        : base(dbContext, announcementService)
    {
    }

    public async Task OnGetAsync()
    {
        await EmployeeEngagementSchema.EnsureAsync(DbContext);
        await LoadAnnouncementsAsync();
        await LoadPollsAsync();
        await LoadFeedbackAsync();
    }
}
