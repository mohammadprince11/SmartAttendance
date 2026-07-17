using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Application.Announcements.Models;
using SmartAttendance.Application.Announcements.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Engagement;

/// <summary>
/// Shared loading, lookups, and display helpers for the Engagement hub pages
/// (announcements, polls, feedback, recognition). Each page loads only what it needs.
/// </summary>
public abstract class EngagementPageModel : PageModel
{
    protected readonly ApplicationDbContext DbContext;
    protected readonly IAnnouncementService AnnouncementService;

    protected EngagementPageModel(
        ApplicationDbContext dbContext,
        IAnnouncementService announcementService)
    {
        DbContext = dbContext;
        AnnouncementService = announcementService;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public List<AnnouncementRow> Announcements { get; protected set; } = new();
    public List<PollRow> Polls { get; protected set; } = new();
    public List<FeedbackRow> FeedbackItems { get; protected set; } = new();
    public List<EmployeeOption> Employees { get; protected set; } = new();
    public List<DepartmentOption> Departments { get; protected set; } = new();
    public List<BranchOption> Branches { get; protected set; } = new();
    public IReadOnlyList<AnnouncementTemplateDefinition> AnnouncementTemplates { get; } = AnnouncementTemplateDefinition.All;

    public int TotalAnnouncements => Announcements.Count;
    public int PublishedAnnouncements => Announcements.Count(x => x.Status.Equals("Published", StringComparison.OrdinalIgnoreCase));
    public int DraftAnnouncements => Announcements.Count(x => x.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase));

    public int TotalPolls => Polls.Count;
    public int PublishedPolls => Polls.Count(x => x.IsPublished);
    public int DraftPolls => Polls.Count(x => !x.IsPublished);
    public int TotalPollVotes => Polls.Sum(x => x.VotesCount);

    public int TotalFeedback => FeedbackItems.Count;
    public int OpenFeedback => FeedbackItems.Count(x => x.Status.Equals("Open", StringComparison.OrdinalIgnoreCase));
    public int PendingFeedback => FeedbackItems.Count(x => x.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase));
    public int AnsweredFeedback => FeedbackItems.Count(x => x.Status.Equals("Answered", StringComparison.OrdinalIgnoreCase) || x.Status.Equals("Closed", StringComparison.OrdinalIgnoreCase));

    public int ActiveEngagementItems => PublishedAnnouncements + PublishedPolls;
    public int OpenEngagementCases => OpenFeedback + PendingFeedback;

    public IReadOnlyList<AnnouncementRow> RecognitionAnnouncements =>
        Announcements
            .Where(x => x.TemplateKey.Equals("appreciation", StringComparison.OrdinalIgnoreCase)
                     || x.TemplateKey.Equals("promotion", StringComparison.OrdinalIgnoreCase)
                     || x.TemplateKey.Equals("welcome", StringComparison.OrdinalIgnoreCase))
            .ToList();

    public IReadOnlyList<AnnouncementRow> CampaignAnnouncements =>
        Announcements
            .Where(x => x.TemplateKey.Equals("circular", StringComparison.OrdinalIgnoreCase)
                     || x.TemplateKey.Equals("workhours", StringComparison.OrdinalIgnoreCase)
                     || x.TemplateKey.Equals("holiday", StringComparison.OrdinalIgnoreCase))
            .ToList();

    protected async Task LoadAnnouncementsAsync()
    {
        var items = await AnnouncementService.GetManagementListAsync(
            Search,
            HttpContext.RequestAborted);

        Announcements = items
            .Select(item => new AnnouncementRow
            {
                Id = item.Id,
                Title = item.Title,
                Body = item.Body,
                Category = item.Category,
                TargetType = item.AudienceSummary,
                TargetValue = item.AudienceSummary,
                TemplateKey = ResolveAnnouncementTemplateKey(item.Category, item.Title),
                Status = item.Status.ToString(),
                IsPublished = item.Status == SmartAttendance.Domain.Enums.AnnouncementStatus.Published,
                PublishDate = item.PublishDate.HasValue
                    ? item.PublishDate.Value.ToDateTime(TimeOnly.MinValue)
                    : null,
                CreatedBy = item.CreatedBy,
                RecipientCount = item.RecipientCount,
                IsLegacy = item.IsLegacy
            })
            .ToList();
    }

    protected async Task LoadPollsAsync()
    {
        var where = string.IsNullOrWhiteSpace(Search) ? string.Empty : "WHERE p.Title LIKE @Search OR p.Question LIKE @Search OR p.Category LIKE @Search";
        Polls = await HrmsDatabase.QueryAsync(
            DbContext,
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

    protected async Task LoadFeedbackAsync()
    {
        var where = string.IsNullOrWhiteSpace(Search) ? string.Empty : "WHERE f.Title LIKE @Search OR f.Message LIKE @Search OR e.FullName LIKE @Search";
        FeedbackItems = await HrmsDatabase.QueryAsync(
            DbContext,
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

    protected async Task LoadAudienceOptionsAsync()
    {
        Employees = await HrmsDatabase.QueryAsync(
            DbContext,
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

        Departments = await HrmsDatabase.QueryAsync(
            DbContext,
            "SELECT Id, Name FROM Departments ORDER BY Name;",
            null,
            reader => new DepartmentOption { Id = HrmsDatabase.GetInt(reader, "Id"), Name = HrmsDatabase.GetString(reader, "Name") });

        Branches = await HrmsDatabase.QueryAsync(
            DbContext,
            "SELECT Id, Name FROM Branches ORDER BY Name;",
            null,
            reader => new BranchOption { Id = HrmsDatabase.GetInt(reader, "Id"), Name = HrmsDatabase.GetString(reader, "Name") });
    }

    protected string? ValidateTarget(string targetType, int[]? employeeIds, int? departmentId, int? branchId)
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

    protected static string BuildTargetValue(string targetType, int[]? employeeIds, int? departmentId, int? branchId)
    {
        if (targetType.Equals("All", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        if (targetType.Equals("Employee", StringComparison.OrdinalIgnoreCase)) return string.Join(',', (employeeIds ?? Array.Empty<int>()).Where(x => x > 0).Distinct());
        if (targetType.Equals("Department", StringComparison.OrdinalIgnoreCase)) return departmentId?.ToString() ?? string.Empty;
        if (targetType.Equals("Branch", StringComparison.OrdinalIgnoreCase)) return branchId?.ToString() ?? string.Empty;
        return string.Empty;
    }

    protected string NormalizeTemplateKey(string? key)
    {
        var normalized = string.IsNullOrWhiteSpace(key) ? "custom" : key.Trim().ToLowerInvariant();
        return AnnouncementTemplates.Any(x => x.Key.Equals(normalized, StringComparison.OrdinalIgnoreCase)) ? normalized : "custom";
    }

    protected AnnouncementTemplateDefinition GetTemplate(string? key)
    {
        var normalized = NormalizeTemplateKey(key);
        return AnnouncementTemplates.First(x => x.Key.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public string TemplateName(string? key) => GetTemplate(key).Name;
    public string TemplateIcon(string? key) => GetTemplate(key).Icon;
    public string TemplateCss(string? key) => GetTemplate(key).CssClass;

    protected AnnouncementActorContext BuildAnnouncementActor() =>
        new()
        {
            UserName = User.Identity?.Name ?? "System",
            Role = Request.Cookies["SA.Role"] ?? string.Empty,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
        };

    protected static string ResolveAnnouncementTemplateKey(string? category, string? title)
    {
        var categoryText = category?.Trim() ?? string.Empty;
        var titleText = title?.Trim() ?? string.Empty;

        if (categoryText.Contains("عطلة", StringComparison.OrdinalIgnoreCase)) return "holiday";
        if (categoryText.Contains("تعميم", StringComparison.OrdinalIgnoreCase)) return "circular";
        if (categoryText.Contains("تعليمات", StringComparison.OrdinalIgnoreCase) ||
            titleText.Contains("الدوام", StringComparison.OrdinalIgnoreCase)) return "workhours";
        if (categoryText.Contains("ترحيب", StringComparison.OrdinalIgnoreCase)) return "welcome";
        if (categoryText.Contains("شكر", StringComparison.OrdinalIgnoreCase)) return "appreciation";
        if (categoryText.Contains("تعزية", StringComparison.OrdinalIgnoreCase)) return "condolence";
        if (categoryText.Contains("وداع", StringComparison.OrdinalIgnoreCase)) return "farewell";

        if (categoryText.Contains("تهنئة", StringComparison.OrdinalIgnoreCase))
        {
            if (titleText.Contains("ترقية", StringComparison.OrdinalIgnoreCase)) return "promotion";
            if (titleText.Contains("مولود", StringComparison.OrdinalIgnoreCase)) return "newborn";
            if (titleText.Contains("زواج", StringComparison.OrdinalIgnoreCase)) return "marriage";
        }

        return "custom";
    }

    public string DisplayDate(DateTime? date) => date.HasValue ? date.Value.ToString("dd/MM/yyyy") : "-";
    public string StatusText(string status) => status.Equals("Open", StringComparison.OrdinalIgnoreCase) ? "مفتوحة" : status.Equals("Pending", StringComparison.OrdinalIgnoreCase) ? "قيد المعالجة" : status.Equals("Answered", StringComparison.OrdinalIgnoreCase) ? "تم الرد" : status.Equals("Closed", StringComparison.OrdinalIgnoreCase) ? "مغلقة" : status;
    public string StatusClass(string status) => status.Equals("Open", StringComparison.OrdinalIgnoreCase) ? "open" : status.Equals("Pending", StringComparison.OrdinalIgnoreCase) ? "pending" : status.Equals("Answered", StringComparison.OrdinalIgnoreCase) ? "answered" : status.Equals("Closed", StringComparison.OrdinalIgnoreCase) ? "closed" : string.Empty;
    public string PublishText(bool isPublished) => isPublished ? "منشور" : "مسودة";
    public string AnnouncementStatusText(string status) => status switch
    {
        "Published" => "منشور",
        "Pending" => "مسودة",
        "Scheduled" => "مجدول",
        "Expired" => "منتهي",
        "Archived" => "مؤرشف",
        _ => status
    };
    public string AnnouncementStatusClass(string status) => status switch
    {
        "Published" => "published",
        "Pending" => "draft",
        "Scheduled" => "pending",
        "Expired" => "neutral",
        "Archived" => "neutral",
        _ => "neutral"
    };
    public string TargetText(AnnouncementRow a) =>
        string.IsNullOrWhiteSpace(a.TargetValue) ? "جمهور محدد" : a.TargetValue;
    public string TargetText(PollRow p) => TargetText(p.TargetType);
    private static string TargetText(string targetType)
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
        public string TemplateKey { get; set; } = "custom";
        public string UseTemplateMode { get; set; } = "Template";
        public string PersonName { get; set; } = string.Empty;
        public string SecondaryName { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string EffectiveDateText { get; set; } = string.Empty;
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
        public string TemplateKey { get; set; } = "custom";
        public string Status { get; set; } = "Pending";
        public bool IsPublished { get; set; }
        public DateTime? PublishDate { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public int RecipientCount { get; set; }
        public bool IsLegacy { get; set; }
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

    public class AnnouncementTemplateDefinition
    {
        public string Key { get; init; } = "custom";
        public string Name { get; init; } = "مخصص";
        public string Description { get; init; } = string.Empty;
        public string Category { get; init; } = "عام";
        public string Icon { get; init; } = "📣";
        public string CssClass { get; init; } = "custom";
        public string AssetKey { get; init; } = "custom";

        public static IReadOnlyList<AnnouncementTemplateDefinition> All { get; } = new List<AnnouncementTemplateDefinition>
        {
            new() { Key = "custom", Name = "مخصص", Description = "إعلان حر", Category = "عام", Icon = "📣", CssClass = "custom", AssetKey = "custom" },
            new() { Key = "holiday", Name = "عطلة رسمية", Description = "إشعار عطلة", Category = "عطلة رسمية", Icon = "📅", CssClass = "holiday", AssetKey = "holiday" },
            new() { Key = "circular", Name = "تعميم إداري", Description = "توجيه رسمي", Category = "تعميم إداري", Icon = "📄", CssClass = "circular", AssetKey = "circular" },
            new() { Key = "workhours", Name = "تغيير الدوام", Description = "تحديث أوقات العمل", Category = "تعليمات", Icon = "🕘", CssClass = "holiday", AssetKey = "holiday" },
            new() { Key = "welcome", Name = "موظف جديد", Description = "ترحيب وانضمام", Category = "ترحيب", Icon = "🤝", CssClass = "welcome", AssetKey = "welcome" },
            new() { Key = "promotion", Name = "ترقية", Description = "تهنئة وظيفية", Category = "تهنئة", Icon = "📈", CssClass = "promotion", AssetKey = "promotion" },
            new() { Key = "appreciation", Name = "شكر وتقدير", Description = "تكريم إنجاز", Category = "شكر وتقدير", Icon = "🏆", CssClass = "promotion", AssetKey = "promotion" },
            new() { Key = "marriage", Name = "زواج", Description = "تهنئة رسمية", Category = "تهنئة", Icon = "💍", CssClass = "marriage", AssetKey = "marriage" },
            new() { Key = "condolence", Name = "تعزية", Description = "تعزية ومواساة", Category = "تعزية", Icon = "🕊️", CssClass = "condolence", AssetKey = "condolence" },
            new() { Key = "newborn", Name = "مولود جديد", Description = "تهنئة مولود", Category = "تهنئة", Icon = "👶", CssClass = "newborn", AssetKey = "newborn" },
            new() { Key = "farewell", Name = "وداع موظف", Description = "شكر وتقدير", Category = "وداع", Icon = "🧳", CssClass = "farewell", AssetKey = "farewell" }
        };
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
