using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Application.Announcements.Services;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

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

    /// <summary>
    /// الشاشة صارت تبويباً بـ<c>/Engagement</c>. يبقى المسار حيّاً لروابطٍ قديمة
    /// أو مفضّلات، ويعيد التوجيه بدل أن يعرض نسخةً ثانية من الشاشة نفسها.
    /// أما معالجات الـPOST أدناه فتبقى **مالكة المنطق** وتُرسل إليها الشاشة الموحّدة.
    /// </summary>
    public IActionResult OnGet() => RedirectToPage("/Engagement/Index", new { tab = "cases" });

    public async Task<IActionResult> OnPostReplyAsync()
    {
        await EmployeeEngagementSchema.EnsureAsync(DbContext);

        if (FeedbackReply.Id <= 0 || string.IsNullOrWhiteSpace(FeedbackReply.Reply))
        {
            StatusMessage = "يرجى كتابة الرد قبل الحفظ.";
            return RedirectToPage("/Engagement/Index", new { tab = "cases" });
        }

        var user = User.Identity?.Name ?? "HR";
        var status = string.IsNullOrWhiteSpace(FeedbackReply.Status) ? "Answered" : FeedbackReply.Status.Trim();
        var scope = await GetCompanyScopeAsync();
        if (!await EmployeeCompanyGuard.CanAccessOwnedRowAsync(
                DbContext, "EmployeeFeedbackItems", "Id", FeedbackReply.Id, scope,
                HttpContext.RequestAborted))
        {
            return NotFound();
        }
        var companyFilter = EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId");

        await HrmsDatabase.ExecuteAsync(
            DbContext,
            $"""
UPDATE f
SET AdminReply = @Reply,
    RepliedBy = @RepliedBy,
    RepliedAt = SYSUTCDATETIME(),
    Status = @Status
FROM EmployeeFeedbackItems f
INNER JOIN Employees e ON e.Id=f.EmployeeId
WHERE f.Id = @Id AND {companyFilter};

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
        return RedirectToPage("/Engagement/Index", new { tab = "cases" });
    }

    public async Task<IActionResult> OnPostCloseAsync(int id)
    {
        await EmployeeEngagementSchema.EnsureAsync(DbContext);
        var user = User.Identity?.Name ?? "HR";
        var scope = await GetCompanyScopeAsync();
        if (!await EmployeeCompanyGuard.CanAccessOwnedRowAsync(
                DbContext, "EmployeeFeedbackItems", "Id", id, scope,
                HttpContext.RequestAborted))
        {
            return NotFound();
        }
        var companyFilter = EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId");

        await HrmsDatabase.ExecuteAsync(
            DbContext,
            $"""
UPDATE f
SET Status = 'Closed',
    RepliedBy = COALESCE(NULLIF(RepliedBy, ''), @UserName),
    RepliedAt = COALESCE(RepliedAt, SYSUTCDATETIME()),
    AdminReply = COALESCE(NULLIF(AdminReply, ''), N'تم إغلاق الطلب من قبل مسؤول النظام.')
FROM EmployeeFeedbackItems f
INNER JOIN Employees e ON e.Id=f.EmployeeId
WHERE f.Id = @Id AND {companyFilter};
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@UserName", user);
            });

        StatusMessage = "تم إغلاق الطلب.";
        return RedirectToPage("/Engagement/Index", new { tab = "cases" });
    }
}
