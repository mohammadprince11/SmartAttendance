using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Application.Announcements.Models;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Engagement;

/// <summary>
/// Write/action handlers owned by the unified Engagement screen.
/// The former Announcements/Polls/Feedback Razor pages were removed.
/// </summary>
public partial class IndexModel
{
[BindProperty]
    public AnnouncementInput Announcement { get; set; } = new();

public async Task<IActionResult> OnPostAnnouncementCreateAsync()
    {
        var title = Announcement.Title?.Trim() ?? string.Empty;
        var body = Announcement.Body?.Trim() ?? string.Empty;
        var templateKey = NormalizeTemplateKey(Announcement.TemplateKey);
        var template = GetTemplate(templateKey);

        if (!templateKey.Equals("custom", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(Announcement.Category) ||
                Announcement.Category.Equals("عام", StringComparison.OrdinalIgnoreCase))
            {
                Announcement.Category = template.Category;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = BuildTemplateTitle(
                    templateKey,
                    Announcement.PersonName);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                body = BuildTemplateBody(
                    templateKey,
                    Announcement.PersonName,
                    Announcement.SecondaryName,
                    Announcement.DepartmentName,
                    Announcement.EffectiveDateText);
            }
        }

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
        {
            StatusMessage = "يرجى إدخال عنوان ووصف الإعلان.";
            return RedirectToPage("/Engagement/Index", new { tab = "announcements" });
        }

        var targetType = string.IsNullOrWhiteSpace(Announcement.TargetType)
            ? "All"
            : Announcement.TargetType.Trim();

        var targetError = ValidateTarget(
            targetType,
            Announcement.EmployeeIds,
            Announcement.DepartmentId,
            Announcement.BranchId);

        if (!string.IsNullOrWhiteSpace(targetError))
        {
            StatusMessage = targetError;
            return RedirectToPage("/Engagement/Index", new { tab = "announcements" });
        }

        if (!await IsTargetWithinCompanyScopeAsync(
                targetType,
                Announcement.EmployeeIds,
                Announcement.DepartmentId,
                Announcement.BranchId))
        {
            StatusMessage = "الجهة المستهدفة خارج نطاق شركاتك.";
            return RedirectToPage("/Engagement/Index", new { tab = "announcements" });
        }

        var scope = await GetCompanyScopeAsync();

        var request = new AnnouncementCreateRequest
        {
            LanguageCode = "ar",
            Title = title,
            Body = body,
            Category = string.IsNullOrWhiteSpace(Announcement.Category)
                ? "عام"
                : Announcement.Category.Trim(),
            PublishNow = Announcement.PublishNow,
            CommentsEnabled = false,
            ReactionsEnabled = true,
            AllEmployees = targetType.Equals("All", StringComparison.OrdinalIgnoreCase) && scope.IsUnrestricted,
            CompanyIds = targetType.Equals("All", StringComparison.OrdinalIgnoreCase) && !scope.IsUnrestricted
                ? scope.AllowedCompanyIds.ToArray()
                : Array.Empty<int>(),
            BranchIds = targetType.Equals("Branch", StringComparison.OrdinalIgnoreCase) &&
                        Announcement.BranchId.HasValue
                ? new[] { Announcement.BranchId.Value }
                : Array.Empty<int>(),
            DepartmentIds = targetType.Equals("Department", StringComparison.OrdinalIgnoreCase) &&
                            Announcement.DepartmentId.HasValue
                ? new[] { Announcement.DepartmentId.Value }
                : Array.Empty<int>(),
            EmployeeIds = targetType.Equals("Employee", StringComparison.OrdinalIgnoreCase)
                ? (Announcement.EmployeeIds ?? Array.Empty<int>())
                : Array.Empty<int>()
        };

        var result = await AnnouncementService.CreateAsync(
            request,
            BuildAnnouncementActor(),
            HttpContext.RequestAborted);

        StatusMessage = result.Message;
        return RedirectToPage("/Engagement/Index", new { tab = "announcements" });
    }

    public async Task<IActionResult> OnPostAnnouncementArchiveAsync(int id)
    {
        if (!await CanManageAnnouncementAsync(id)) return NotFound();

        var result = await AnnouncementService.ArchiveAsync(
            id,
            BuildAnnouncementActor(),
            HttpContext.RequestAborted);

        StatusMessage = result.Message;
        return RedirectToPage("/Engagement/Index", new { tab = "announcements" });
    }

    public async Task<IActionResult> OnPostAnnouncementToggleAsync(int id, bool publish)
    {
        if (!await CanManageAnnouncementAsync(id)) return NotFound();

        var result = publish
            ? await AnnouncementService.PublishAsync(
                id,
                BuildAnnouncementActor(),
                HttpContext.RequestAborted)
            : await AnnouncementService.ArchiveAsync(
                id,
                BuildAnnouncementActor(),
                HttpContext.RequestAborted);

        StatusMessage = result.Message;
        return RedirectToPage("/Engagement/Index", new { tab = "announcements" });
    }

    private string BuildTemplateTitle(string templateKey, string? personName)
    {
        var name = string.IsNullOrWhiteSpace(personName) ? "أحد موظفينا" : personName.Trim();
        return templateKey switch
        {
            "holiday" => "إعلان عطلة رسمية",
            "circular" => "تعميم إداري",
            "workhours" => "تغيير أوقات الدوام",
            "welcome" => $"ترحيب بموظف جديد - {name}",
            "promotion" => $"تهنئة بالترقية - {name}",
            "appreciation" => $"شكر وتقدير - {name}",
            "marriage" => $"تهنئة بمناسبة الزواج - {name}",
            "condolence" => $"تعزية ومواساة - {name}",
            "newborn" => $"مولود جديد - {name}",
            "farewell" => $"وداع وشكر - {name}",
            "birthday" => $"عيد ميلاد سعيد - {name}",
            "employee-of-month" => $"موظف الشهر - {name}",
            "anniversary" => $"ذكرى عمل - {name}",
            "retirement" => $"تقاعد وتكريم - {name}",
            _ => Announcement.Title?.Trim() ?? string.Empty
        };
    }

    private string BuildTemplateBody(string templateKey, string? personName, string? secondaryName, string? departmentName, string? effectiveDateText)
    {
        var name = string.IsNullOrWhiteSpace(personName) ? "زميلنا العزيز" : personName.Trim();
        var extra = string.IsNullOrWhiteSpace(secondaryName) ? string.Empty : secondaryName.Trim();
        var department = string.IsNullOrWhiteSpace(departmentName) ? "قسم الموارد البشرية" : departmentName.Trim();
        var date = string.IsNullOrWhiteSpace(effectiveDateText) ? string.Empty : effectiveDateText.Trim();

        return templateKey switch
        {
            "holiday" => $"تعلن إدارة الموارد البشرية عن عطلة رسمية{(string.IsNullOrWhiteSpace(date) ? "" : $" بتاريخ {date}")}. يرجى من جميع الموظفين الالتزام بالتعليمات الخاصة بالدوام والعودة حسب التوجيه المعتمد.",
            "circular" => $"يرجى من جميع الموظفين الاطلاع على هذا التعميم الإداري والعمل بمضمونه اعتباراً{(string.IsNullOrWhiteSpace(date) ? " من تاريخ النشر" : $" من تاريخ {date}")}. لأي استفسار يرجى التواصل مع الجهة المعنية.",
            "workhours" => $"نود إعلامكم بأنه تم تحديث أوقات الدوام{(string.IsNullOrWhiteSpace(extra) ? "" : $" إلى {extra}")}{(string.IsNullOrWhiteSpace(date) ? "" : $" اعتباراً من {date}")}. يرجى الالتزام بالتوقيت الجديد والتنسيق مع المسؤول المباشر.",
            "welcome" => $"نرحب بانضمام {name} إلى فريق العمل{(string.IsNullOrWhiteSpace(extra) ? "" : $" بمنصب {extra}")}. نتمنى له بداية موفقة ومسيرة ناجحة ضمن عائلة الشركة.",
            "promotion" => $"نبارك إلى {name} ترقيته{(string.IsNullOrWhiteSpace(extra) ? "" : $" إلى منصب {extra}")}. نتمنى له دوام النجاح والتوفيق في مهامه الجديدة، مع الشكر والتقدير لجهوده المميزة.",
            "appreciation" => $"تتقدم الشركة بالشكر والتقدير إلى {name}{(string.IsNullOrWhiteSpace(extra) ? "" : $" تقديراً لـ {extra}")}. نثمّن هذا العطاء ونتمنى له المزيد من النجاح والتميز.",
            "marriage" => $"تتقدم الشركة بخالص التهاني والتبريكات إلى {name} بمناسبة الزواج. نتمنى له حياة سعيدة مليئة بالمودة والنجاح، ودوام الفرح والتوفيق.",
            "condolence" => $"تتقدم الشركة بخالص العزاء والمواساة إلى {name}. سائلين الله أن يتغمد الفقيد بواسع رحمته، وأن يلهم أهله وذويه الصبر والسلوان.",
            "newborn" => $"تتقدم الشركة بأصدق التهاني إلى {name} بمناسبة المولود الجديد{(string.IsNullOrWhiteSpace(extra) ? "" : $" ({extra})")}. نسأل الله أن يجعله من مواليد السعادة والبركة.",
            "farewell" => $"تتقدم الشركة بالشكر والتقدير إلى {name} على ما قدمه من جهود خلال فترة عمله. نتمنى له دوام التوفيق والنجاح في مسيرته القادمة.",
            "birthday" => $"تتقدم الشركة بأطيب التهاني إلى {name} بمناسبة عيد ميلاده{(string.IsNullOrWhiteSpace(date) ? "" : $" بتاريخ {date}")}. كل عام وهو بخير وصحة وسعادة.",
            "employee-of-month" => $"يسرنا تكريم {name} بلقب موظف الشهر{(string.IsNullOrWhiteSpace(extra) ? "" : $" تقديراً لـ {extra}")}. نثمّن تميزه والتزامه، ونتمنى له مزيداً من النجاح.",
            "anniversary" => $"نبارك إلى {name} ذكرى انضمامه لفريق العمل{(string.IsNullOrWhiteSpace(extra) ? "" : $" — {extra}")}{(string.IsNullOrWhiteSpace(date) ? "" : $" بتاريخ {date}")}. شكراً لعطائه المستمر خلال هذه المسيرة.",
            "retirement" => $"تتقدم الشركة بخالص الشكر والتقدير إلى {name} بمناسبة تقاعده، عرفاناً بسنوات العطاء والإخلاص. نتمنى له تقاعداً سعيداً مليئاً بالصحة والراحة.",
            _ => Announcement.Body?.Trim() ?? string.Empty
        } + $"\n\n{department}";
    }

[BindProperty]
    public PollInput Poll { get; set; } = new();

public async Task<IActionResult> OnPostPollCreateAsync()
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
            return RedirectToPage("/Engagement/Index", new { tab = "polls" });
        }

        var targetType = string.IsNullOrWhiteSpace(Poll.TargetType) ? "All" : Poll.TargetType.Trim();
        var targetError = ValidateTarget(targetType, Poll.EmployeeIds, Poll.DepartmentId, Poll.BranchId);
        if (!string.IsNullOrWhiteSpace(targetError))
        {
            StatusMessage = targetError;
            return RedirectToPage("/Engagement/Index", new { tab = "polls" });
        }

        if (!await IsTargetWithinCompanyScopeAsync(
                targetType, Poll.EmployeeIds, Poll.DepartmentId, Poll.BranchId))
        {
            StatusMessage = "الجهة المستهدفة خارج نطاق شركاتك.";
            return RedirectToPage("/Engagement/Index", new { tab = "polls" });
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

        // الاستطلاع الجديد يُنسب لشركة منشئه فينعزل؛ غير المقيَّد يُنشئ مشتركاً كالسابق.
        await ConfigTenantScope.AssignCompanyAsync(
            DbContext,
            ConfigTenantScope.EmployeePolls,
            pollId,
            ConfigTenantScope.OwningCompany(await GetCompanyScopeAsync()));

        StatusMessage = isPublished ? "تم نشر الاستطلاع وسيظهر للموظفين حسب الجهة المستهدفة." : "تم حفظ الاستطلاع كمسودة.";
        return RedirectToPage("/Engagement/Index", new { tab = "polls" });
    }

    public async Task<IActionResult> OnPostPollToggleAsync(int id, bool publish)
    {
        await EmployeeEngagementSchema.EnsureAsync(DbContext);

        // نشر/سحب استطلاع شركة أخرى بمعرّفٍ من المتصفّح — حارس ملكية مغلق الفشل.
        if (!await ConfigTenantScope.IsInScopeAsync(
                DbContext, ConfigTenantScope.EmployeePolls, id, await GetCompanyScopeAsync()))
        {
            return NotFound();
        }

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
        return RedirectToPage("/Engagement/Index", new { tab = "polls" });
    }

    public async Task<IActionResult> OnPostPollDeleteAsync(int id)
    {
        await EmployeeEngagementSchema.EnsureAsync(DbContext);

        // الحذف يطال الأصوات والخيارات أيضاً — أثرٌ لا رجعة فيه على شركة أخرى.
        if (!await ConfigTenantScope.IsInScopeAsync(
                DbContext, ConfigTenantScope.EmployeePolls, id, await GetCompanyScopeAsync()))
        {
            return NotFound();
        }

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
        return RedirectToPage("/Engagement/Index", new { tab = "polls" });
    }

[BindProperty]
    public FeedbackReplyInput FeedbackReply { get; set; } = new();

public async Task<IActionResult> OnPostFeedbackReplyAsync()
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

    public async Task<IActionResult> OnPostFeedbackCloseAsync(int id)
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