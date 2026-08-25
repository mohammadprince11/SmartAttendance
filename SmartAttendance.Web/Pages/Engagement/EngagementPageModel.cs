using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Application.Announcements.Models;
using SmartAttendance.Application.Announcements.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

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
        var scope = await GetCompanyScopeAsync();
        var items = await AnnouncementService.GetManagementListAsync(
            Search,
            new AnnouncementManagementScope
            {
                IsUnrestricted = scope.IsUnrestricted,
                AllowedCompanyIds = scope.AllowedCompanyIds.ToArray()
            },
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

    /// <summary>
    /// نطاق شركات الطلب. يُحلّ من <c>RequestServices</c> لا بالحقن بالمُنشئ كي لا
    /// تتغيّر تواقيع الصفحات الخمس الوارثة لأجل حاجةٍ يخصّ الاستطلاعات وحدها.
    /// </summary>
    protected Task<CompanyScope> GetCompanyScopeAsync() =>
        HttpContext.RequestServices.GetRequiredService<ICompanyScopeProvider>().GetAsync();

    protected async Task LoadPollsAsync()
    {
        // العرض محصورٌ بالنطاق: المشترك (CompanyId NULL) + ما يخصّ شركات المستخدم.
        //
        // ⚠️ الأقواس حول شرط البحث ليست تجميلاً: بدونها يصير الشرط
        // `WHERE scope AND Title LIKE .. OR Question LIKE ..` — و`OR` أضعف ارتباطاً
        // من `AND` فينفكّ حصر النطاق ويُسرّب استطلاعات الشركات الأخرى عند أي بحث.
        var scopeClause = (await GetCompanyScopeAsync()).ToSharedConfigSqlPredicate("p.CompanyId");
        var where = string.IsNullOrWhiteSpace(Search)
            ? $"WHERE {scopeClause}"
            : $"WHERE {scopeClause} AND (p.Title LIKE @Search OR p.Question LIKE @Search OR p.Category LIKE @Search)";
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
        var scope = await GetCompanyScopeAsync();
        var scopeFilter = EmployeeCompanyGuard.ListFilter(scope, "e.CompanyId");
        var where = string.IsNullOrWhiteSpace(Search)
            ? $"WHERE {scopeFilter}"
            : $"WHERE {scopeFilter} AND (f.Title LIKE @Search OR f.Message LIKE @Search OR e.FullName LIKE @Search)";
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
        // ⛔ لم تعد تُحمَّل قائمة الموظفين هنا. كانت `SELECT TOP 500 … ORDER BY
        //    FullName` تغذّي `<select multiple>` — وعندنا 1357 موظفاً نشطاً، أي
        //    أن **857 موظفاً لم يكونوا قابلين للاستهداف أصلاً** ولا إشارة اقتطاع.
        //    محلّها `_EmployeePickerMulti` ببحثٍ خادميّ بلا سقفٍ صامت.
        //    (الخاصية باقية لأن `Announcements.cshtml`/`Polls.cshtml` تشيران إليها،
        //     وهما صفحتا معالِجات لا واجهة — `OnGet` فيهما يعيد التوجيه هنا.)
        Employees = new List<EmployeeOption>();

        var scope = await GetCompanyScopeAsync();
        var departmentScope = EmployeeCompanyGuard.ListFilter(scope, "CompanyId");
        var branchScope = EmployeeCompanyGuard.ListFilter(scope, "CompanyId");

        Departments = await HrmsDatabase.QueryAsync(
            DbContext,
            $"SELECT Id, Name FROM Departments WHERE {departmentScope} ORDER BY Name;",
            null,
            reader => new DepartmentOption { Id = HrmsDatabase.GetInt(reader, "Id"), Name = HrmsDatabase.GetString(reader, "Name") });

        Branches = await HrmsDatabase.QueryAsync(
            DbContext,
            $"SELECT Id, Name FROM Branches WHERE {branchScope} ORDER BY Name;",
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

    protected async Task<bool> IsTargetWithinCompanyScopeAsync(
        string targetType,
        int[]? employeeIds,
        int? departmentId,
        int? branchId)
    {
        var scope = await GetCompanyScopeAsync();
        if (scope.IsUnrestricted) return true;

        if (targetType.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            return !scope.IsDeniedAll;
        }

        if (targetType.Equals("Employee", StringComparison.OrdinalIgnoreCase))
        {
            var requested = (employeeIds ?? Array.Empty<int>()).Where(id => id > 0).Distinct().ToArray();
            var allowed = await EmployeeCompanyGuard.FilterEmployeesInScopeAsync(
                DbContext, requested, scope, HttpContext.RequestAborted);
            return requested.Length > 0 && allowed.Count == requested.Length;
        }

        if (targetType.Equals("Department", StringComparison.OrdinalIgnoreCase) && departmentId is > 0)
        {
            var count = await HrmsDatabase.ScalarAsync<int>(
                DbContext,
                $"SELECT COUNT(*) FROM Departments WHERE Id=@Id AND {EmployeeCompanyGuard.ListFilter(scope, "CompanyId")};",
                command => HrmsDatabase.AddParameter(command, "@Id", departmentId.Value));
            return count == 1;
        }

        if (targetType.Equals("Branch", StringComparison.OrdinalIgnoreCase) && branchId is > 0)
        {
            var count = await HrmsDatabase.ScalarAsync<int>(
                DbContext,
                $"SELECT COUNT(*) FROM Branches WHERE Id=@Id AND {EmployeeCompanyGuard.ListFilter(scope, "CompanyId")};",
                command => HrmsDatabase.AddParameter(command, "@Id", branchId.Value));
            return count == 1;
        }

        return false;
    }

    protected async Task<bool> CanManageAnnouncementAsync(int announcementId)
    {
        var scope = await GetCompanyScopeAsync();
        if (scope.IsUnrestricted) return true;
        if (announcementId <= 0 || scope.IsDeniedAll) return false;

        var allowed = scope.AllowedCompanyIds.OrderBy(id => id).ToArray();
        var companyFilter = string.Join(", ", allowed);
        var count = await HrmsDatabase.ScalarAsync<int>(
            DbContext,
            $"""
SELECT COUNT(*)
FROM AnnouncementGroups g
WHERE g.Id=@Id AND ISNULL(g.IsDeleted,0)=0
  AND EXISTS (
      SELECT 1 FROM AnnouncementAudienceRules r
      LEFT JOIN Branches b ON b.Id=r.BranchId
      LEFT JOIN Departments d ON d.Id=r.DepartmentId
      LEFT JOIN HrJobPositions p ON p.Id=r.PositionId
      LEFT JOIN Employees e ON e.Id=r.EmployeeId
      WHERE r.AnnouncementGroupId=g.Id AND r.IsExcluded=0
        AND (r.CompanyId IN ({companyFilter}) OR b.CompanyId IN ({companyFilter})
             OR d.CompanyId IN ({companyFilter}) OR p.CompanyId IN ({companyFilter})
             OR e.CompanyId IN ({companyFilter})))
  AND NOT EXISTS (
      SELECT 1 FROM AnnouncementAudienceRules r
      LEFT JOIN Branches b ON b.Id=r.BranchId
      LEFT JOIN Departments d ON d.Id=r.DepartmentId
      LEFT JOIN HrJobPositions p ON p.Id=r.PositionId
      LEFT JOIN Employees e ON e.Id=r.EmployeeId
      WHERE r.AnnouncementGroupId=g.Id AND r.IsExcluded=0
        AND (r.AudienceType=1 OR
             (r.CompanyId IS NOT NULL AND r.CompanyId NOT IN ({companyFilter})) OR
             (r.BranchId IS NOT NULL AND b.CompanyId NOT IN ({companyFilter})) OR
             (r.DepartmentId IS NOT NULL AND d.CompanyId NOT IN ({companyFilter})) OR
             (r.PositionId IS NOT NULL AND p.CompanyId NOT IN ({companyFilter})) OR
             (r.EmployeeId IS NOT NULL AND (e.CompanyId IS NULL OR e.CompanyId NOT IN ({companyFilter})))))
""",
            command => HrmsDatabase.AddParameter(command, "@Id", announcementId));

        return count == 1;
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

    /// <summary>
    /// ⚠️ الدور يُقرأ من **مطالبة الهوية** لا من كوكي `SA.Role`.
    ///
    /// **هذا إصلاحٌ أمنيّ: كوكي `SA.Role` كان مدخلَ تخويلٍ يتحكّم به العميل.**
    /// وهو كوكي **ميت**: يُحذف بتسجيل الدخول والخروج و**لا يُكتب بأي مكان
    /// بالمستودع إطلاقاً** (بقيّة من مخطّط مصادقة قديم). فكانت له نتيجتان
    /// متقابلتان، كلتاهما من نفس السطر:
    ///
    ///   • **تصعيد صلاحيات**: أيّ مستخدمٍ مسجَّل دخوله يكتب <c>SA.Role=Admin</c>
    ///     بمتصفّحه فيمرّ من اختصار الأدمن بـ<c>AnnouncementService</c>
    ///     (<c>HasPermissionAsync</c>) ويتجاوز جدول <c>SystemUserPermissions</c>
    ///     كلياً — إنشاء الإعلانات والاستطلاعات ونشرها وإلغاء نشرها وأرشفتها.
    ///   • **وتعطيلٌ للمودل**: الأدمن الحقيقي لا يملك الكوكي فيُرفَض برسالة
    ///     «ليس لديك صلاحية» (رُصد بمحاولة نشر إعلان حيّاً 2026-08-02).
    ///
    /// و<c>ClaimTypes.Role</c> هو مصدر الدور المعتمد ببقية النظام:
    /// <c>PeopleAccessContext</c> · <c>RoleSecurityMiddleware</c> ·
    /// <c>NotificationBellViewComponent</c> — وتكتبه صفحة الدخول ويحدّثه
    /// <c>SessionClaimsRefresher</c>. ومسار الصلاحيات الجدوليّ لغير الأدمن يبقى
    /// كما هو بلا تغيير.
    /// </summary>
    protected AnnouncementActorContext BuildAnnouncementActor() =>
        new()
        {
            UserName = User.Identity?.Name ?? "System",
            Role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
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
            new() { Key = "farewell", Name = "وداع موظف", Description = "شكر وتقدير", Category = "وداع", Icon = "🧳", CssClass = "farewell", AssetKey = "farewell" },
            // أربعة قوالب أُضيفت لمطابقة طقم كيان الكامل (دراسة 2026-08-15).
            new() { Key = "birthday", Name = "عيد ميلاد", Description = "تهنئة عيد ميلاد", Category = "تهنئة", Icon = "🎂", CssClass = "newborn", AssetKey = "newborn" },
            new() { Key = "employee-of-month", Name = "موظف الشهر", Description = "تكريم شهري", Category = "شكر وتقدير", Icon = "🌟", CssClass = "promotion", AssetKey = "promotion" },
            new() { Key = "anniversary", Name = "ذكرى عمل", Description = "ذكرى انضمام", Category = "تهنئة", Icon = "🎖️", CssClass = "promotion", AssetKey = "promotion" },
            new() { Key = "retirement", Name = "تقاعد موظف", Description = "تكريم تقاعد", Category = "وداع", Icon = "🏅", CssClass = "farewell", AssetKey = "farewell" }
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
