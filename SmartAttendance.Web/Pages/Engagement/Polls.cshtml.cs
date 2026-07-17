using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Application.Announcements.Services;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Engagement;

public class PollsModel : EngagementPageModel
{
    public PollsModel(
        ApplicationDbContext dbContext,
        IAnnouncementService announcementService)
        : base(dbContext, announcementService)
    {
    }

    [BindProperty]
    public PollInput Poll { get; set; } = new();

    public async Task OnGetAsync()
    {
        await EmployeeEngagementSchema.EnsureAsync(DbContext);
        await LoadAudienceOptionsAsync();
        await LoadPollsAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        await EmployeeEngagementSchema.EnsureAsync(DbContext);

        var title = Poll.Title?.Trim() ?? string.Empty;
        var question = Poll.Question?.Trim() ?? string.Empty;
        var options = (Poll.OptionsText ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(question) || options.Count < 2)
        {
            StatusMessage = "يرجى إدخال عنوان وسؤال وخيارين على الأقل للاستطلاع.";
            return RedirectToPage();
        }

        var targetType = string.IsNullOrWhiteSpace(Poll.TargetType) ? "All" : Poll.TargetType.Trim();
        var targetError = ValidateTarget(targetType, Poll.EmployeeIds, Poll.DepartmentId, Poll.BranchId);
        if (!string.IsNullOrWhiteSpace(targetError))
        {
            StatusMessage = targetError;
            return RedirectToPage();
        }

        var targetValue = BuildTargetValue(targetType, Poll.EmployeeIds, Poll.DepartmentId, Poll.BranchId);
        var category = string.IsNullOrWhiteSpace(Poll.Category) ? "استطلاع" : Poll.Category.Trim();
        var isPublished = Poll.PublishNow;
        var user = User.Identity?.Name ?? "HR";

        var pollId = await HrmsDatabase.ScalarAsync<int>(
            DbContext,
            """
INSERT INTO EmployeePolls
(Title, Question, Category, TargetType, TargetValue, IsPublished, PublishDate, CreatedBy, CreatedAt)
VALUES
(@Title, @Question, @Category, @TargetType, @TargetValue, @IsPublished, SYSUTCDATETIME(), @CreatedBy, SYSUTCDATETIME());
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Title", title);
                HrmsDatabase.AddParameter(command, "@Question", question);
                HrmsDatabase.AddParameter(command, "@Category", category);
                HrmsDatabase.AddParameter(command, "@TargetType", targetType);
                HrmsDatabase.AddParameter(command, "@TargetValue", targetValue);
                HrmsDatabase.AddParameter(command, "@IsPublished", isPublished);
                HrmsDatabase.AddParameter(command, "@CreatedBy", user);
            });

        for (var i = 0; i < options.Count; i++)
        {
            await HrmsDatabase.ExecuteAsync(
                DbContext,
                """
INSERT INTO EmployeePollOptions (PollId, OptionText, DisplayOrder)
VALUES (@PollId, @OptionText, @DisplayOrder);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@PollId", pollId);
                    HrmsDatabase.AddParameter(command, "@OptionText", options[i]);
                    HrmsDatabase.AddParameter(command, "@DisplayOrder", i + 1);
                });
        }

        await HrmsDatabase.ExecuteAsync(
            DbContext,
            """
INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('EmployeePoll', CAST(@PollId AS nvarchar(80)), 'Create Poll', @NewValues, @UserName, @IpAddress);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@PollId", pollId);
                HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(("Title", title), ("Published", isPublished), ("Options", options.Count)));
                HrmsDatabase.AddParameter(command, "@UserName", user);
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });

        StatusMessage = isPublished ? "تم نشر الاستطلاع وسيظهر للموظفين حسب الجهة المستهدفة." : "تم حفظ الاستطلاع كمسودة.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id, bool publish)
    {
        await EmployeeEngagementSchema.EnsureAsync(DbContext);
        var user = User.Identity?.Name ?? "HR";

        await HrmsDatabase.ExecuteAsync(
            DbContext,
            """
UPDATE EmployeePolls
SET IsPublished = @Publish,
    PublishDate = CASE WHEN @Publish = 1 THEN SYSUTCDATETIME() ELSE PublishDate END
WHERE Id = @Id;

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('EmployeePoll', CAST(@Id AS nvarchar(80)), 'Toggle Poll Publish', @NewValues, @UserName, @IpAddress);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@Publish", publish);
                HrmsDatabase.AddParameter(command, "@NewValues", publish ? "Published" : "Draft");
                HrmsDatabase.AddParameter(command, "@UserName", user);
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });

        StatusMessage = publish ? "تم نشر الاستطلاع." : "تم تحويل الاستطلاع إلى مسودة.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await EmployeeEngagementSchema.EnsureAsync(DbContext);
        var user = User.Identity?.Name ?? "HR";

        await HrmsDatabase.ExecuteAsync(
            DbContext,
            """
DELETE FROM EmployeePollVotes WHERE PollId = @Id;
DELETE FROM EmployeePollOptions WHERE PollId = @Id;
DELETE FROM EmployeePolls WHERE Id = @Id;

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('EmployeePoll', CAST(@Id AS nvarchar(80)), 'Delete Poll', 'Deleted from Engagement polls page', @UserName, @IpAddress);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@UserName", user);
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });

        StatusMessage = "تم حذف الاستطلاع من قاعدة البيانات.";
        return RedirectToPage();
    }
}
