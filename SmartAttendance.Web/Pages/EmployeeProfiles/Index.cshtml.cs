using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.EmployeeProfiles;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string Section { get; set; } = "engagement";

    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "announcements";

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty]
    public AnnouncementInput Announcement { get; set; } = new();

    [BindProperty]
    public PollInput Poll { get; set; } = new();

    [BindProperty]
    public FeedbackReplyInput FeedbackReply { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public List<AnnouncementRow> Announcements { get; private set; } = new();
    public List<PollRow> Polls { get; private set; } = new();
    public List<FeedbackRow> FeedbackItems { get; private set; } = new();
    public List<EmployeeOption> Employees { get; private set; } = new();
    public List<DepartmentOption> Departments { get; private set; } = new();
    public List<BranchOption> Branches { get; private set; } = new();

    public int TotalAnnouncements => Announcements.Count;
    public int PublishedAnnouncements => Announcements.Count(x => x.IsPublished);
    public int DraftAnnouncements => Announcements.Count(x => !x.IsPublished);
    public int WallPosts => Announcements.Count(x => x.IsPublished);

    public int TotalPolls => Polls.Count;
    public int PublishedPolls => Polls.Count(x => x.IsPublished);
    public int DraftPolls => Polls.Count(x => !x.IsPublished);
    public int TotalPollVotes => Polls.Sum(x => x.VotesCount);

    public int TotalFeedback => FeedbackItems.Count;
    public int OpenFeedback => FeedbackItems.Count(x => x.Status.Equals("Open", StringComparison.OrdinalIgnoreCase));
    public int PendingFeedback => FeedbackItems.Count(x => x.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase));
    public int AnsweredFeedback => FeedbackItems.Count(x => x.Status.Equals("Answered", StringComparison.OrdinalIgnoreCase) || x.Status.Equals("Closed", StringComparison.OrdinalIgnoreCase));

    public async Task OnGetAsync()
    {
        NormalizeRoute();
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostCreateAnnouncementAsync()
    {
        await EnsureEngagementTablesAsync();

        var title = Announcement.Title?.Trim() ?? string.Empty;
        var body = Announcement.Body?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body))
        {
            StatusMessage = "يرجى إدخال عنوان ووصف الإعلان.";
            return RedirectToPage(new { section = "engagement", tab = "announcements" });
        }

        var targetType = string.IsNullOrWhiteSpace(Announcement.TargetType) ? "All" : Announcement.TargetType.Trim();
        var targetError = ValidateTarget(targetType, Announcement.EmployeeIds, Announcement.DepartmentId, Announcement.BranchId);
        if (!string.IsNullOrWhiteSpace(targetError))
        {
            StatusMessage = targetError;
            return RedirectToPage(new { section = "engagement", tab = "announcements" });
        }

        var targetValue = BuildTargetValue(targetType, Announcement.EmployeeIds, Announcement.DepartmentId, Announcement.BranchId);
        var category = string.IsNullOrWhiteSpace(Announcement.Category) ? "عام" : Announcement.Category.Trim();
        var isPublished = Announcement.PublishNow;
        var user = Request.Cookies["SA.UserName"] ?? "HR";

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
INSERT INTO EmployeePortalAnnouncements
(Title, Body, Category, TargetType, TargetValue, IsPublished, PublishDate, CreatedBy, CreatedAt)
VALUES
(@Title, @Body, @Category, @TargetType, @TargetValue, @IsPublished, SYSUTCDATETIME(), @CreatedBy, SYSUTCDATETIME());

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('EmployeePortalAnnouncement', NULL, 'Create Announcement', @NewValues, @UserName, @IpAddress);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Title", title);
                HrmsDatabase.AddParameter(command, "@Body", body);
                HrmsDatabase.AddParameter(command, "@Category", category);
                HrmsDatabase.AddParameter(command, "@TargetType", targetType);
                HrmsDatabase.AddParameter(command, "@TargetValue", targetValue);
                HrmsDatabase.AddParameter(command, "@IsPublished", isPublished);
                HrmsDatabase.AddParameter(command, "@CreatedBy", user);
                HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(
                    ("Title", title),
                    ("Category", category),
                    ("TargetType", targetType),
                    ("TargetValue", targetValue),
                    ("Published", isPublished)));
                HrmsDatabase.AddParameter(command, "@UserName", user);
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });

        StatusMessage = isPublished ? "تم نشر الإعلان وسيظهر في حائط الموظف حسب الجهة المستهدفة." : "تم حفظ الإعلان كمسودة.";
        return RedirectToPage(new { section = "engagement", tab = "announcements" });
    }

    public async Task<IActionResult> OnPostDeleteAnnouncementAsync(int id)
    {
        await EnsureEngagementTablesAsync();
        var user = Request.Cookies["SA.UserName"] ?? "HR";

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
DELETE FROM EmployeePortalAnnouncements WHERE Id = @Id;

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('EmployeePortalAnnouncement', CAST(@Id AS nvarchar(80)), 'Delete Announcement', 'Deleted from HR operations page', @UserName, @IpAddress);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@UserName", user);
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });

        StatusMessage = "تم حذف الإعلان من قاعدة البيانات.";
        return RedirectToPage(new { section = "engagement", tab = "announcements" });
    }

    public async Task<IActionResult> OnPostToggleAnnouncementAsync(int id, bool publish)
    {
        await EnsureEngagementTablesAsync();
        var user = Request.Cookies["SA.UserName"] ?? "HR";

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
UPDATE EmployeePortalAnnouncements
SET IsPublished = @Publish,
    PublishDate = CASE WHEN @Publish = 1 THEN SYSUTCDATETIME() ELSE PublishDate END
WHERE Id = @Id;

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('EmployeePortalAnnouncement', CAST(@Id AS nvarchar(80)), 'Toggle Announcement Publish', @NewValues, @UserName, @IpAddress);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@Publish", publish);
                HrmsDatabase.AddParameter(command, "@NewValues", publish ? "Published" : "Draft");
                HrmsDatabase.AddParameter(command, "@UserName", user);
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });

        StatusMessage = publish ? "تم نشر الإعلان." : "تم تحويل الإعلان إلى مسودة.";
        return RedirectToPage(new { section = "engagement", tab = "announcements" });
    }

    public async Task<IActionResult> OnPostCreatePollAsync()
    {
        await EnsureEngagementTablesAsync();

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
            return RedirectToPage(new { section = "engagement", tab = "polls" });
        }

        var targetType = string.IsNullOrWhiteSpace(Poll.TargetType) ? "All" : Poll.TargetType.Trim();
        var targetError = ValidateTarget(targetType, Poll.EmployeeIds, Poll.DepartmentId, Poll.BranchId);
        if (!string.IsNullOrWhiteSpace(targetError))
        {
            StatusMessage = targetError;
            return RedirectToPage(new { section = "engagement", tab = "polls" });
        }

        var targetValue = BuildTargetValue(targetType, Poll.EmployeeIds, Poll.DepartmentId, Poll.BranchId);
        var category = string.IsNullOrWhiteSpace(Poll.Category) ? "استطلاع" : Poll.Category.Trim();
        var isPublished = Poll.PublishNow;
        var user = Request.Cookies["SA.UserName"] ?? "HR";

        var pollId = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
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
                _dbContext,
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
            _dbContext,
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
        return RedirectToPage(new { section = "engagement", tab = "polls" });
    }

    public async Task<IActionResult> OnPostTogglePollAsync(int id, bool publish)
    {
        await EnsureEngagementTablesAsync();
        var user = Request.Cookies["SA.UserName"] ?? "HR";

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
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
        return RedirectToPage(new { section = "engagement", tab = "polls" });
    }

    public async Task<IActionResult> OnPostDeletePollAsync(int id)
    {
        await EnsureEngagementTablesAsync();
        var user = Request.Cookies["SA.UserName"] ?? "HR";

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
DELETE FROM EmployeePollVotes WHERE PollId = @Id;
DELETE FROM EmployeePollOptions WHERE PollId = @Id;
DELETE FROM EmployeePolls WHERE Id = @Id;

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('EmployeePoll', CAST(@Id AS nvarchar(80)), 'Delete Poll', 'Deleted from HR operations page', @UserName, @IpAddress);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@UserName", user);
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });

        StatusMessage = "تم حذف الاستطلاع من قاعدة البيانات.";
        return RedirectToPage(new { section = "engagement", tab = "polls" });
    }

    public async Task<IActionResult> OnPostReplyFeedbackAsync()
    {
        await EnsureEngagementTablesAsync();

        if (FeedbackReply.Id <= 0 || string.IsNullOrWhiteSpace(FeedbackReply.Reply))
        {
            StatusMessage = "يرجى كتابة الرد قبل الحفظ.";
            return RedirectToPage(new { section = "engagement", tab = "feedback" });
        }

        var user = Request.Cookies["SA.UserName"] ?? "HR";
        var status = string.IsNullOrWhiteSpace(FeedbackReply.Status) ? "Answered" : FeedbackReply.Status.Trim();

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
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
        return RedirectToPage(new { section = "engagement", tab = "feedback" });
    }

    public async Task<IActionResult> OnPostCloseFeedbackAsync(int id)
    {
        await EnsureEngagementTablesAsync();
        var user = Request.Cookies["SA.UserName"] ?? "HR";

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
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
        return RedirectToPage(new { section = "engagement", tab = "feedback" });
    }

    private async Task LoadAsync()
    {
        await EnsureEngagementTablesAsync();
        Employees = await LoadEmployeesAsync();
        Departments = await LoadDepartmentsAsync();
        Branches = await LoadBranchesAsync();
        Announcements = await LoadAnnouncementsAsync();
        Polls = await LoadPollsAsync();
        FeedbackItems = await LoadFeedbackAsync();
    }

    private void NormalizeRoute()
    {
        if (string.IsNullOrWhiteSpace(Section)) Section = "engagement";
        if (string.IsNullOrWhiteSpace(Tab)) Tab = "announcements";
        Section = Section.Trim().ToLowerInvariant();
        Tab = Tab.Trim().ToLowerInvariant();
    }

    private async Task EnsureEngagementTablesAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
IF OBJECT_ID('EmployeePortalAnnouncements', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeePortalAnnouncements
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Title nvarchar(250) NOT NULL,
        Body nvarchar(max) NULL,
        Category nvarchar(80) NULL,
        TargetType nvarchar(50) NULL,
        TargetValue nvarchar(max) NULL,
        IsPublished bit NOT NULL DEFAULT(1),
        PublishDate datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(150) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF COL_LENGTH('EmployeePortalAnnouncements', 'TargetValue') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('EmployeePortalAnnouncements') AND name = 'TargetValue' AND max_length > 0 AND max_length < 1000)
        ALTER TABLE EmployeePortalAnnouncements ALTER COLUMN TargetValue nvarchar(max) NULL;
END;

IF OBJECT_ID('EmployeeFeedbackItems', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeFeedbackItems
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        Type nvarchar(50) NOT NULL,
        Title nvarchar(250) NOT NULL,
        Message nvarchar(max) NULL,
        Priority nvarchar(50) NULL,
        Status nvarchar(50) NOT NULL DEFAULT('Open'),
        AdminReply nvarchar(max) NULL,
        RepliedBy nvarchar(150) NULL,
        RepliedAt datetime2 NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('EmployeeCompensations', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeCompensations
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        BasicSalary decimal(18,2) NULL,
        Allowances decimal(18,2) NULL,
        Deductions decimal(18,2) NULL,
        PaymentMethod nvarchar(80) NULL,
        BankName nvarchar(150) NULL,
        BankAccount nvarchar(150) NULL,
        Currency nvarchar(30) NULL,
        UpdatedAt datetime2 NULL
    );
END;

IF OBJECT_ID('EmployeePolls', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeePolls
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Title nvarchar(250) NOT NULL,
        Question nvarchar(max) NULL,
        Category nvarchar(80) NULL,
        TargetType nvarchar(50) NULL,
        TargetValue nvarchar(max) NULL,
        IsPublished bit NOT NULL DEFAULT(1),
        PublishDate datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        CreatedBy nvarchar(150) NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF OBJECT_ID('EmployeePollOptions', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeePollOptions
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PollId int NOT NULL,
        OptionText nvarchar(300) NOT NULL,
        DisplayOrder int NOT NULL DEFAULT(1)
    );
END;

IF OBJECT_ID('EmployeePollVotes', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeePollVotes
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PollId int NOT NULL,
        OptionId int NOT NULL,
        EmployeeId int NOT NULL,
        VotedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_EmployeePollVotes_PollEmployee' AND object_id = OBJECT_ID('EmployeePollVotes'))
BEGIN
    CREATE UNIQUE INDEX UX_EmployeePollVotes_PollEmployee ON EmployeePollVotes(PollId, EmployeeId);
END;

""");
    }

    private async Task<List<AnnouncementRow>> LoadAnnouncementsAsync()
    {
        var where = string.IsNullOrWhiteSpace(Search) ? string.Empty : "WHERE Title LIKE @Search OR Body LIKE @Search OR Category LIKE @Search";
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            $"""
SELECT TOP 100
    Id,
    Title,
    ISNULL(Body, '') AS Body,
    ISNULL(Category, N'عام') AS Category,
    ISNULL(TargetType, N'All') AS TargetType,
    ISNULL(TargetValue, '') AS TargetValue,
    IsPublished,
    PublishDate,
    ISNULL(CreatedBy, '') AS CreatedBy
FROM EmployeePortalAnnouncements
{where}
ORDER BY PublishDate DESC, Id DESC;
""",
            command =>
            {
                if (!string.IsNullOrWhiteSpace(Search))
                {
                    HrmsDatabase.AddParameter(command, "@Search", $"%{Search.Trim()}%");
                }
            },
            reader => new AnnouncementRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Title = HrmsDatabase.GetString(reader, "Title"),
                Body = HrmsDatabase.GetString(reader, "Body"),
                Category = HrmsDatabase.GetString(reader, "Category"),
                TargetType = HrmsDatabase.GetString(reader, "TargetType"),
                TargetValue = HrmsDatabase.GetString(reader, "TargetValue"),
                IsPublished = HrmsDatabase.GetBool(reader, "IsPublished"),
                PublishDate = HrmsDatabase.GetDateTime(reader, "PublishDate"),
                CreatedBy = HrmsDatabase.GetString(reader, "CreatedBy")
            });
    }

    private async Task<List<PollRow>> LoadPollsAsync()
    {
        var where = string.IsNullOrWhiteSpace(Search) ? string.Empty : "WHERE p.Title LIKE @Search OR p.Question LIKE @Search OR p.Category LIKE @Search";
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            $"""
SELECT TOP 100
    p.Id,
    p.Title,
    ISNULL(p.Question, '') AS Question,
    ISNULL(p.Category, N'استطلاع') AS Category,
    ISNULL(p.TargetType, N'All') AS TargetType,
    ISNULL(p.TargetValue, '') AS TargetValue,
    p.IsPublished,
    p.PublishDate,
    ISNULL(p.CreatedBy, '') AS CreatedBy,
    (SELECT COUNT(1) FROM EmployeePollOptions o WHERE o.PollId = p.Id) AS OptionsCount,
    (SELECT COUNT(1) FROM EmployeePollVotes v WHERE v.PollId = p.Id) AS VotesCount
FROM EmployeePolls p
{where}
ORDER BY p.PublishDate DESC, p.Id DESC;
""",
            command =>
            {
                if (!string.IsNullOrWhiteSpace(Search))
                {
                    HrmsDatabase.AddParameter(command, "@Search", $"%{Search.Trim()}%");
                }
            },
            reader => new PollRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Title = HrmsDatabase.GetString(reader, "Title"),
                Question = HrmsDatabase.GetString(reader, "Question"),
                Category = HrmsDatabase.GetString(reader, "Category"),
                TargetType = HrmsDatabase.GetString(reader, "TargetType"),
                TargetValue = HrmsDatabase.GetString(reader, "TargetValue"),
                IsPublished = HrmsDatabase.GetBool(reader, "IsPublished"),
                PublishDate = HrmsDatabase.GetDateTime(reader, "PublishDate"),
                CreatedBy = HrmsDatabase.GetString(reader, "CreatedBy"),
                OptionsCount = HrmsDatabase.GetInt(reader, "OptionsCount"),
                VotesCount = HrmsDatabase.GetInt(reader, "VotesCount")
            });
    }

    private async Task<List<FeedbackRow>> LoadFeedbackAsync()
    {
        var where = string.IsNullOrWhiteSpace(Search) ? string.Empty : "WHERE f.Title LIKE @Search OR f.Message LIKE @Search OR e.FullName LIKE @Search";
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            $"""
SELECT TOP 100
    f.Id,
    f.EmployeeId,
    ISNULL(e.EmployeeNo, '') AS EmployeeNo,
    ISNULL(e.FullName, '') AS EmployeeName,
    f.Type,
    f.Title,
    ISNULL(f.Message, '') AS Message,
    ISNULL(f.Priority, '') AS Priority,
    f.Status,
    ISNULL(f.AdminReply, '') AS AdminReply,
    ISNULL(f.RepliedBy, '') AS RepliedBy,
    f.RepliedAt,
    f.CreatedAt
FROM EmployeeFeedbackItems f
LEFT JOIN Employees e ON e.Id = f.EmployeeId
{where}
ORDER BY f.CreatedAt DESC, f.Id DESC;
""",
            command =>
            {
                if (!string.IsNullOrWhiteSpace(Search))
                {
                    HrmsDatabase.AddParameter(command, "@Search", $"%{Search.Trim()}%");
                }
            },
            reader => new FeedbackRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "EmployeeName"),
                Type = HrmsDatabase.GetString(reader, "Type"),
                Title = HrmsDatabase.GetString(reader, "Title"),
                Message = HrmsDatabase.GetString(reader, "Message"),
                Priority = HrmsDatabase.GetString(reader, "Priority"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                AdminReply = HrmsDatabase.GetString(reader, "AdminReply"),
                RepliedBy = HrmsDatabase.GetString(reader, "RepliedBy"),
                RepliedAt = HrmsDatabase.GetDateTime(reader, "RepliedAt"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });
    }

    private async Task<List<EmployeeOption>> LoadEmployeesAsync() => await HrmsDatabase.QueryAsync(
        _dbContext,
        """
SELECT TOP 500 Id, EmployeeNo, FullName
FROM Employees
WHERE IsActive = 1
ORDER BY FullName;
""",
        null,
        reader => new EmployeeOption
        {
            Id = HrmsDatabase.GetInt(reader, "Id"),
            EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
            FullName = HrmsDatabase.GetString(reader, "FullName")
        });

    private async Task<List<DepartmentOption>> LoadDepartmentsAsync() => await HrmsDatabase.QueryAsync(
        _dbContext,
        "SELECT Id, Name FROM Departments ORDER BY Name;",
        null,
        reader => new DepartmentOption { Id = HrmsDatabase.GetInt(reader, "Id"), Name = HrmsDatabase.GetString(reader, "Name") });

    private async Task<List<BranchOption>> LoadBranchesAsync() => await HrmsDatabase.QueryAsync(
        _dbContext,
        "SELECT Id, Name FROM Branches ORDER BY Name;",
        null,
        reader => new BranchOption { Id = HrmsDatabase.GetInt(reader, "Id"), Name = HrmsDatabase.GetString(reader, "Name") });

    private string? ValidateTarget(string targetType, int[]? employeeIds, int? departmentId, int? branchId)
    {
        if (targetType.Equals("Employee", StringComparison.OrdinalIgnoreCase))
        {
            var selectedEmployees = (employeeIds ?? Array.Empty<int>()).Where(x => x > 0).Distinct().ToArray();
            return selectedEmployees.Length == 0 ? "يرجى اختيار موظف واحد على الأقل عند توجيه الإعلان أو الاستطلاع إلى موظفين محددين." : null;
        }

        if (targetType.Equals("Department", StringComparison.OrdinalIgnoreCase) && (!departmentId.HasValue || departmentId.Value <= 0))
        {
            return "يرجى اختيار القسم عند توجيه الإعلان أو الاستطلاع إلى قسم محدد.";
        }

        if (targetType.Equals("Branch", StringComparison.OrdinalIgnoreCase) && (!branchId.HasValue || branchId.Value <= 0))
        {
            return "يرجى اختيار الفرع عند توجيه الإعلان أو الاستطلاع إلى فرع محدد.";
        }

        return null;
    }

    private string BuildTargetValue(string targetType, int[]? employeeIds, int? departmentId, int? branchId)
    {
        if (targetType.Equals("All", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        if (targetType.Equals("Employee", StringComparison.OrdinalIgnoreCase)) return string.Join(',', (employeeIds ?? Array.Empty<int>()).Where(x => x > 0).Distinct());
        if (targetType.Equals("Department", StringComparison.OrdinalIgnoreCase)) return departmentId?.ToString() ?? string.Empty;
        if (targetType.Equals("Branch", StringComparison.OrdinalIgnoreCase)) return branchId?.ToString() ?? string.Empty;
        return string.Empty;
    }

    public string DisplayDate(DateTime? date) => date.HasValue ? date.Value.ToString("dd/MM/yyyy") : "-";
    public string StatusText(string status) => status.Equals("Open", StringComparison.OrdinalIgnoreCase) ? "مفتوحة" : status.Equals("Pending", StringComparison.OrdinalIgnoreCase) ? "قيد المعالجة" : status.Equals("Answered", StringComparison.OrdinalIgnoreCase) ? "تم الرد" : status.Equals("Closed", StringComparison.OrdinalIgnoreCase) ? "مغلقة" : status;
    public string StatusClass(string status) => status.Equals("Open", StringComparison.OrdinalIgnoreCase) ? "open" : status.Equals("Pending", StringComparison.OrdinalIgnoreCase) ? "pending" : status.Equals("Answered", StringComparison.OrdinalIgnoreCase) ? "answered" : status.Equals("Closed", StringComparison.OrdinalIgnoreCase) ? "closed" : string.Empty;
    public string PublishText(bool isPublished) => isPublished ? "منشور" : "مسودة";
    public string TargetText(AnnouncementRow a) => TargetText(a.TargetType);
    public string TargetText(PollRow p) => TargetText(p.TargetType);
    private string TargetText(string targetType)
    {
        if (targetType.Equals("All", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(targetType)) return "جميع الموظفين";
        if (targetType.Equals("Employee", StringComparison.OrdinalIgnoreCase)) return "موظفون محددون";
        if (targetType.Equals("Department", StringComparison.OrdinalIgnoreCase)) return "قسم محدد";
        if (targetType.Equals("Branch", StringComparison.OrdinalIgnoreCase)) return "فرع محدد";
        return targetType;
    }

    public class AnnouncementInput
    {
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Category { get; set; } = "عام";
        public string TargetType { get; set; } = "All";
        public int[]? EmployeeIds { get; set; }
        public int? DepartmentId { get; set; }
        public int? BranchId { get; set; }
        public bool PublishNow { get; set; } = true;
    }

    public class PollInput
    {
        public string Title { get; set; } = string.Empty;
        public string Question { get; set; } = string.Empty;
        public string OptionsText { get; set; } = string.Empty;
        public string Category { get; set; } = "استطلاع";
        public string TargetType { get; set; } = "All";
        public int[]? EmployeeIds { get; set; }
        public int? DepartmentId { get; set; }
        public int? BranchId { get; set; }
        public bool PublishNow { get; set; } = true;
    }

    public class FeedbackReplyInput
    {
        public int Id { get; set; }
        public string Reply { get; set; } = string.Empty;
        public string Status { get; set; } = "Answered";
    }

    public class AnnouncementRow
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string TargetType { get; set; } = string.Empty;
        public string TargetValue { get; set; } = string.Empty;
        public bool IsPublished { get; set; }
        public DateTime? PublishDate { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
    }

    public class PollRow
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Question { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string TargetType { get; set; } = string.Empty;
        public string TargetValue { get; set; } = string.Empty;
        public bool IsPublished { get; set; }
        public DateTime? PublishDate { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public int OptionsCount { get; set; }
        public int VotesCount { get; set; }
    }

    public class FeedbackRow
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string AdminReply { get; set; } = string.Empty;
        public string RepliedBy { get; set; } = string.Empty;
        public DateTime? RepliedAt { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    public class EmployeeOption
    {
        public int Id { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
    }

    public class DepartmentOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class BranchOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
