using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Application.Announcements.Services;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Engagement;

public class FeedbackModel : EngagementPageModel
{
    public FeedbackModel(
        ApplicationDbContext dbContext,
        IAnnouncementService announcementService)
        : base(dbContext, announcementService)
    {
    }

    [BindProperty]
    public FeedbackReplyInput FeedbackReply { get; set; } = new();

    public async Task OnGetAsync()
    {
        await EmployeeEngagementSchema.EnsureAsync(DbContext);
        await LoadFeedbackAsync();
    }

    public async Task<IActionResult> OnPostReplyAsync()
    {
        await EmployeeEngagementSchema.EnsureAsync(DbContext);

        if (FeedbackReply.Id <= 0 || string.IsNullOrWhiteSpace(FeedbackReply.Reply))
        {
            StatusMessage = "يرجى كتابة الرد قبل الحفظ.";
            return RedirectToPage();
        }

        var user = User.Identity?.Name ?? "HR";
        var status = string.IsNullOrWhiteSpace(FeedbackReply.Status) ? "Answered" : FeedbackReply.Status.Trim();

        await HrmsDatabase.ExecuteAsync(
            DbContext,
            """
UPDATE EmployeeFeedbackItems
SET AdminReply = @Reply,
    RepliedBy = @RepliedBy,
    RepliedAt = SYSUTCDATETIME(),
    Status = @Status
WHERE Id = @Id;

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('EmployeeFeedbackItems', CAST(@Id AS nvarchar(80)), 'Reply Employee Feedback', @NewValues, @UserName, @IpAddress);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", FeedbackReply.Id);
                HrmsDatabase.AddParameter(command, "@Reply", FeedbackReply.Reply.Trim());
                HrmsDatabase.AddParameter(command, "@RepliedBy", user);
                HrmsDatabase.AddParameter(command, "@Status", status);
                HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(("Status", status), ("Reply", FeedbackReply.Reply)));
                HrmsDatabase.AddParameter(command, "@UserName", user);
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });

        StatusMessage = "تم حفظ الرد وسيظهر للموظف داخل بوابة الموظف.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCloseAsync(int id)
    {
        await EmployeeEngagementSchema.EnsureAsync(DbContext);
        var user = User.Identity?.Name ?? "HR";

        await HrmsDatabase.ExecuteAsync(
            DbContext,
            """
UPDATE EmployeeFeedbackItems
SET Status = 'Closed',
    RepliedBy = COALESCE(NULLIF(RepliedBy, ''), @UserName),
    RepliedAt = COALESCE(RepliedAt, SYSUTCDATETIME()),
    AdminReply = COALESCE(NULLIF(AdminReply, ''), N'تم إغلاق الطلب من قبل مسؤول النظام.')
WHERE Id = @Id;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@UserName", user);
            });

        StatusMessage = "تم إغلاق الطلب.";
        return RedirectToPage();
    }
}
