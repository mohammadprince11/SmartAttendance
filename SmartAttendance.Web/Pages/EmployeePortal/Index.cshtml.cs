using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Application.Announcements.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.EmployeePortal;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAnnouncementService _announcementService;

    public IndexModel(
        ApplicationDbContext dbContext,
        IAnnouncementService announcementService)
    {
        _dbContext = dbContext;
        _announcementService = announcementService;
    }

    public string Tab { get; private set; } = "home";
    public EmployeePortalEmployee Employee { get; private set; } = EmployeePortalEmployee.Empty;
    public EmployeePortalCompensation Compensation { get; private set; } = new();
    public List<EmployeePortalAnnouncement> Announcements { get; private set; } = new();
    public List<EmployeePortalPoll> Polls { get; private set; } = new();
    public List<EmployeePortalRequest> Requests { get; private set; } = new();
    public List<EmployeePortalAttendance> Attendance { get; private set; } = new();
    public List<EmployeePortalTeamMember> Team { get; private set; } = new();
    public List<EmployeePortalFeedback> FeedbackItems { get; private set; } = new();

    [BindProperty]
    public FeedbackInput Feedback { get; set; } = new();

    [BindProperty]
    public PollVoteInput PollVote { get; set; } = new();

    [BindProperty]
    public SelfServiceRequestInput RequestInput { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public bool IsDemoMode { get; private set; }
    public string Initials => GetInitials(Employee.FullName);
    public int PendingRequestsCount => Requests.Count(x => !IsFinalStatus(x.Status));
    public int OpenFeedbackCount => FeedbackItems.Count(x => x.Status.Equals("Open", StringComparison.OrdinalIgnoreCase) || x.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase));
    public int CompletedAttendanceCount => Attendance.Count(x => x.CheckOut.HasValue);
    public int MissingCheckoutCount => Attendance.Count(x => !x.CheckOut.HasValue);
    public int OpenPollsCount => Polls.Count(x => !x.HasVoted);
    public string LastAttendanceText => Attendance.FirstOrDefault()?.AttendanceDate is DateTime d ? DisplayDate(d) : "لا يوجد";
    public string AttendanceTodayStatus => Attendance.FirstOrDefault()?.Status is string s && !string.IsNullOrWhiteSpace(s) ? s : "بانتظار السجل";
    public string ServicePeriodText => BuildServicePeriod(Employee.HireDate);
    public string CompensationNote => Compensation.HasData ? "بيانات التعويضات مدخلة في النظام." : "لا توجد بيانات تعويضات مدخلة لهذا الموظف حالياً.";
    public string EmployeeInsight => MissingCheckoutCount > 0 ? "يوجد سجلات حضور تحتاج مراجعة" : OpenPollsCount > 0 ? "يوجد استبيان بانتظار مشاركتك" : "لا توجد إجراءات عاجلة حالياً";

    public async Task<IActionResult> OnGetAsync(string? tab)
    {
        Tab = NormalizeTab(tab);
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostReadAnnouncementAsync(
        int id,
        string? returnTab)
    {
        var employeeId = await ResolveEmployeeIdAsync();

        if (employeeId <= 0)
        {
            StatusMessage = "لا يمكن تسجيل القراءة لأن المستخدم غير مرتبط بموظف.";
            return RedirectToPage(new { tab = returnTab ?? "home" });
        }

        var result = await _announcementService.MarkReadAsync(
            id,
            employeeId,
            HttpContext.RequestAborted);

        StatusMessage = result.Message;
        return RedirectToPage(new { tab = returnTab ?? "home" });
    }

    public async Task<IActionResult> OnPostFeedbackAsync(string? returnTab)
    {
        await EmployeeEngagementSchema.EnsureAsync(_dbContext);

        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            StatusMessage = "لا يمكن إرسال الطلب لأن المستخدم غير مرتبط بموظف.";
            return RedirectToPage(new { tab = returnTab ?? "feedback" });
        }

        var type = string.IsNullOrWhiteSpace(Feedback.Type) ? "اقتراح" : Feedback.Type.Trim();
        var priority = string.IsNullOrWhiteSpace(Feedback.Priority) ? "متوسط" : Feedback.Priority.Trim();
        var title = Feedback.Title?.Trim() ?? string.Empty;
        var message = Feedback.Message?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
        {
            StatusMessage = "يرجى إدخال العنوان والتفاصيل.";
            return RedirectToPage(new { tab = returnTab ?? "feedback" });
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
INSERT INTO EmployeeFeedbackItems
(EmployeeId, Type, Title, Message, Priority, Status, CreatedAt)
VALUES
(@EmployeeId, @Type, @Title, @Message, @Priority, 'Open', SYSUTCDATETIME());
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Type", type);
                HrmsDatabase.AddParameter(command, "@Title", title);
                HrmsDatabase.AddParameter(command, "@Message", message);
                HrmsDatabase.AddParameter(command, "@Priority", priority);
            });

        StatusMessage = "تم إرسال الطلب وسيظهر لمسؤول النظام أو صاحب الصلاحية للرد عليه.";
        return RedirectToPage(new { tab = returnTab ?? "feedback" });
    }

    public async Task<IActionResult> OnPostVotePollAsync(string? returnTab)
    {
        await EmployeeEngagementSchema.EnsureAsync(_dbContext);

        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            StatusMessage = "لا يمكن التصويت لأن المستخدم غير مرتبط بموظف.";
            return RedirectToPage(new { tab = returnTab ?? "pulse" });
        }

        if (PollVote.PollId <= 0 || PollVote.OptionId <= 0)
        {
            StatusMessage = "يرجى اختيار إجابة قبل الإرسال.";
            return RedirectToPage(new { tab = returnTab ?? "pulse" });
        }

        var exists = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT COUNT(1) FROM EmployeePollVotes WHERE PollId = @PollId AND EmployeeId = @EmployeeId",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@PollId", PollVote.PollId);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
            });

        if (exists > 0)
        {
            StatusMessage = "تم تسجيل تصويتك مسبقاً لهذا الاستطلاع.";
            return RedirectToPage(new { tab = returnTab ?? "pulse" });
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
INSERT INTO EmployeePollVotes (PollId, OptionId, EmployeeId, VotedAt)
VALUES (@PollId, @OptionId, @EmployeeId, SYSUTCDATETIME());
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@PollId", PollVote.PollId);
                HrmsDatabase.AddParameter(command, "@OptionId", PollVote.OptionId);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
            });

        StatusMessage = "تم تسجيل تصويتك بنجاح. شكراً لمشاركتك.";
        return RedirectToPage(new { tab = returnTab ?? "pulse" });
    }


    public async Task<IActionResult> OnPostCreateRequestAsync(string? returnTab)
    {
        await EmployeeEngagementSchema.EnsureAsync(_dbContext);

        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            StatusMessage = "لا يمكن إرسال الطلب لأن المستخدم غير مرتبط بموظف.";
            return RedirectToPage(new { tab = returnTab ?? "requests" });
        }

        var type = (RequestInput.RequestType ?? string.Empty).Trim();
        var fromDate = RequestInput.FromDate;
        var toDate = RequestInput.ToDate;
        var reason = (RequestInput.Reason ?? string.Empty).Trim();

        var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "إجازة",
            "نسيان بصمة",
            "خروج شخصي",
            "خروج عمل",
            "أوفر تايم"
        };

        if (string.IsNullOrWhiteSpace(type) || !allowedTypes.Contains(type))
        {
            StatusMessage = "يرجى اختيار نوع طلب صحيح.";
            return RedirectToPage(new { tab = returnTab ?? "requests" });
        }

        if (!fromDate.HasValue)
        {
            StatusMessage = "يرجى إدخال تاريخ بداية الطلب.";
            return RedirectToPage(new { tab = returnTab ?? "requests" });
        }

        if (toDate.HasValue && toDate.Value.Date < fromDate.Value.Date)
        {
            StatusMessage = "تاريخ النهاية لا يمكن أن يكون قبل تاريخ البداية.";
            return RedirectToPage(new { tab = returnTab ?? "requests" });
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            reason = "تم الإرسال من بوابة الموظف";
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
INSERT INTO SelfServiceRequests
(EmployeeId, RequestType, CreatedAt, FromDate, ToDate, Reason, Status)
VALUES
(@EmployeeId, @RequestType, SYSUTCDATETIME(), @FromDate, @ToDate, @Reason, 'Pending');
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@RequestType", type);
                HrmsDatabase.AddParameter(command, "@FromDate", fromDate.Value);
                HrmsDatabase.AddParameter(command, "@ToDate", toDate);
                HrmsDatabase.AddParameter(command, "@Reason", reason);
            });

        StatusMessage = $"تم إرسال طلب {type} بنجاح وهو الآن قيد المراجعة.";
        return RedirectToPage(new { tab = returnTab ?? "requests" });
    }

    private async Task LoadAsync()
    {
        await EmployeeEngagementSchema.EnsureAsync(_dbContext);

        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            IsDemoMode = true;
            employeeId = await HrmsDatabase.ScalarAsync<int>(_dbContext, "SELECT TOP 1 Id FROM Employees ORDER BY Id");
        }

        if (employeeId <= 0)
        {
            Employee = EmployeePortalEmployee.Empty with { FullName = "موظف تجريبي", Position = "Employee" };
            IsDemoMode = true;
            return;
        }

        Employee = await LoadEmployeeAsync(employeeId) ?? EmployeePortalEmployee.Empty;
        Compensation = await LoadCompensationAsync(employeeId);
        Announcements = await LoadAnnouncementsAsync(Employee);
        Polls = await LoadPollsAsync(Employee);
        Requests = await LoadRequestsAsync(employeeId);
        Attendance = await LoadAttendanceAsync(employeeId);
        Team = await LoadTeamAsync(Employee);
        FeedbackItems = await LoadFeedbackAsync(employeeId);
    }

    private async Task<int> ResolveEmployeeIdAsync()
    {
        var employeeIdClaim = User.FindFirstValue("EmployeeId");

        if (int.TryParse(employeeIdClaim, out var claimEmployeeId) && claimEmployeeId > 0)
        {
            return claimEmployeeId;
        }

        var username = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Name);
        if (!string.IsNullOrWhiteSpace(username))
        {
            return await HrmsDatabase.ScalarAsync<int>(
                _dbContext,
                "SELECT TOP 1 ISNULL(EmployeeId, 0) FROM AppLoginUsers WHERE Username = @Username AND IsActive = 1",
                command => HrmsDatabase.AddParameter(command, "@Username", username));
        }

        return 0;
    }

    private async Task<EmployeePortalEmployee?> LoadEmployeeAsync(int employeeId)
    {
        var list = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1
    e.Id,
    e.EmployeeNo,
    e.FullName,
    ISNULL(e.NationalId, '') AS NationalId,
    ISNULL(e.Phone, '') AS Phone,
    ISNULL(e.Email, '') AS Email,
    ISNULL(e.Position, '') AS Position,
    e.HireDate,
    e.BirthDate,
    e.IsActive,
    ISNULL(d.Name, '') AS DepartmentName,
    ISNULL(b.Name, '') AS BranchName,
    '' AS ManagerName
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON d.BranchId = b.Id
WHERE e.Id = @EmployeeId;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new EmployeePortalEmployee
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                NationalId = HrmsDatabase.GetString(reader, "NationalId"),
                Phone = HrmsDatabase.GetString(reader, "Phone"),
                Email = HrmsDatabase.GetString(reader, "Email"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                HireDate = HrmsDatabase.GetDateTime(reader, "HireDate"),
                BirthDate = HrmsDatabase.GetDateTime(reader, "BirthDate"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                DepartmentName = HrmsDatabase.GetString(reader, "DepartmentName"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName"),
                ManagerName = HrmsDatabase.GetString(reader, "ManagerName")
            });

        return list.FirstOrDefault();
    }

    private async Task<EmployeePortalCompensation> LoadCompensationAsync(int employeeId)
    {
        var list = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1
    ISNULL(BasicSalary, 0) AS BasicSalary,
    ISNULL(Allowances, 0) AS Allowances,
    ISNULL(Deductions, 0) AS Deductions,
    ISNULL(PaymentMethod, '') AS PaymentMethod,
    ISNULL(BankName, '') AS BankName,
    ISNULL(BankAccount, '') AS BankAccount,
    ISNULL(Currency, 'IQD') AS Currency
FROM EmployeeCompensations
WHERE EmployeeId = @EmployeeId
ORDER BY UpdatedAt DESC, Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new EmployeePortalCompensation
            {
                BasicSalary = GetDecimal(reader, "BasicSalary"),
                Allowances = GetDecimal(reader, "Allowances"),
                Deductions = GetDecimal(reader, "Deductions"),
                PaymentMethod = HrmsDatabase.GetString(reader, "PaymentMethod"),
                BankName = HrmsDatabase.GetString(reader, "BankName"),
                BankAccount = HrmsDatabase.GetString(reader, "BankAccount"),
                Currency = HrmsDatabase.GetString(reader, "Currency")
            });

        return list.FirstOrDefault() ?? new EmployeePortalCompensation();
    }

    private async Task<List<EmployeePortalAnnouncement>> LoadAnnouncementsAsync(EmployeePortalEmployee employee)
    {
        var items = await _announcementService.GetEmployeeFeedAsync(
            employee.Id,
            HttpContext.RequestAborted);

        return items
            .Select(item => new EmployeePortalAnnouncement
            {
                Id = item.Id,
                Title = item.Title,
                Body = item.Body,
                Category = item.Category,
                TargetType = "RecipientSnapshot",
                TargetValue = employee.Id.ToString(),
                PublishDate = item.PublishDate.HasValue
                    ? item.PublishDate.Value.ToDateTime(TimeOnly.MinValue)
                    : null,
                IsRead = item.IsRead,
                FirstReadAtUtc = item.FirstReadAtUtc
            })
            .ToList();
    }

    private async Task<List<EmployeePortalPoll>> LoadPollsAsync(EmployeePortalEmployee employee)
    {
        var polls = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 10
    p.Id,
    p.Title,
    ISNULL(p.Question, '') AS Question,
    ISNULL(p.Category, N'استطلاع') AS Category,
    ISNULL(p.TargetType, N'All') AS TargetType,
    ISNULL(p.TargetValue, '') AS TargetValue,
    p.PublishDate,
    CASE WHEN EXISTS (SELECT 1 FROM EmployeePollVotes v WHERE v.PollId = p.Id AND v.EmployeeId = @EmployeeId) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS HasVoted
FROM EmployeePolls p
WHERE p.IsPublished = 1
  AND
  (
      p.TargetType IS NULL
      OR p.TargetType = 'All'
      OR (p.TargetType = 'Employee' AND (p.TargetValue = @EmployeeIdText OR p.TargetValue = @EmployeeNo OR p.TargetValue LIKE @EmployeeIdLike))
      OR (p.TargetType = 'Department' AND p.TargetValue = @DepartmentName)
      OR (p.TargetType = 'Branch' AND p.TargetValue = @BranchName)
  )
ORDER BY p.PublishDate DESC, p.Id DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employee.Id);
                HrmsDatabase.AddParameter(command, "@EmployeeIdText", employee.Id.ToString());
                HrmsDatabase.AddParameter(command, "@EmployeeNo", employee.EmployeeNo);
                HrmsDatabase.AddParameter(command, "@EmployeeIdLike", $"%{employee.Id}%");
                HrmsDatabase.AddParameter(command, "@DepartmentName", employee.DepartmentName);
                HrmsDatabase.AddParameter(command, "@BranchName", employee.BranchName);
            },
            reader => new EmployeePortalPoll
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Title = HrmsDatabase.GetString(reader, "Title"),
                Question = HrmsDatabase.GetString(reader, "Question"),
                Category = HrmsDatabase.GetString(reader, "Category"),
                TargetType = HrmsDatabase.GetString(reader, "TargetType"),
                TargetValue = HrmsDatabase.GetString(reader, "TargetValue"),
                PublishDate = HrmsDatabase.GetDateTime(reader, "PublishDate"),
                HasVoted = HrmsDatabase.GetBool(reader, "HasVoted")
            });

        foreach (var poll in polls)
        {
            poll.Options = await LoadPollOptionsAsync(poll.Id);
        }

        return polls;
    }

    private async Task<List<EmployeePortalPollOption>> LoadPollOptionsAsync(int pollId)
    {
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT Id, OptionText, DisplayOrder
FROM EmployeePollOptions
WHERE PollId = @PollId
ORDER BY DisplayOrder, Id;
""",
            command => HrmsDatabase.AddParameter(command, "@PollId", pollId),
            reader => new EmployeePortalPollOption
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                OptionText = HrmsDatabase.GetString(reader, "OptionText"),
                DisplayOrder = HrmsDatabase.GetInt(reader, "DisplayOrder")
            });
    }

    private async Task<List<EmployeePortalRequest>> LoadRequestsAsync(int employeeId)
    {
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 15
    Id,
    RequestType,
    CreatedAt,
    FromDate,
    ToDate,
    ISNULL(Reason, '') AS Reason,
    Status
FROM SelfServiceRequests
WHERE EmployeeId = @EmployeeId
ORDER BY CreatedAt DESC, Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new EmployeePortalRequest
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                RequestType = HrmsDatabase.GetString(reader, "RequestType"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt"),
                FromDate = HrmsDatabase.GetDateTime(reader, "FromDate"),
                ToDate = HrmsDatabase.GetDateTime(reader, "ToDate"),
                Reason = HrmsDatabase.GetString(reader, "Reason"),
                Status = HrmsDatabase.GetString(reader, "Status")
            });
    }

    private async Task<List<EmployeePortalAttendance>> LoadAttendanceAsync(int employeeId)
    {
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 20
    AttendanceDate,
    CheckIn,
    CheckOut,
    CAST(ISNULL(Status, '') AS nvarchar(50)) AS Status,
    CAST(ISNULL(Source, '') AS nvarchar(50)) AS Source,
    ISNULL(Notes, '') AS Notes
FROM AttendanceRecords
WHERE EmployeeId = @EmployeeId
ORDER BY AttendanceDate DESC, Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new EmployeePortalAttendance
            {
                AttendanceDate = HrmsDatabase.GetDateTime(reader, "AttendanceDate"),
                CheckIn = HrmsDatabase.GetDateTime(reader, "CheckIn"),
                CheckOut = HrmsDatabase.GetDateTime(reader, "CheckOut"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                Source = HrmsDatabase.GetString(reader, "Source"),
                Notes = HrmsDatabase.GetString(reader, "Notes")
            });
    }

    private async Task<List<EmployeePortalTeamMember>> LoadTeamAsync(EmployeePortalEmployee employee)
    {
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 8
    e.Id,
    e.EmployeeNo,
    e.FullName,
    ISNULL(e.Position, '') AS Position
FROM Employees e
WHERE e.Id <> @EmployeeId
  AND e.IsActive = 1
ORDER BY e.FullName;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employee.Id),
            reader => new EmployeePortalTeamMember
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                Position = HrmsDatabase.GetString(reader, "Position")
            });
    }

    private async Task<List<EmployeePortalFeedback>> LoadFeedbackAsync(int employeeId)
    {
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 20
    Id,
    Type,
    Title,
    ISNULL(Message, '') AS Message,
    ISNULL(Priority, '') AS Priority,
    Status,
    ISNULL(AdminReply, '') AS AdminReply,
    ISNULL(RepliedBy, '') AS RepliedBy,
    RepliedAt,
    CreatedAt
FROM EmployeeFeedbackItems
WHERE EmployeeId = @EmployeeId
ORDER BY CreatedAt DESC, Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new EmployeePortalFeedback
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
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

    private static decimal GetDecimal(System.Data.Common.DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToDecimal(reader.GetValue(ordinal));
    }

    public string DisplayDate(DateTime? date) => date.HasValue ? date.Value.ToString("dd/MM/yyyy") : "-";
    public string DisplayTime(DateTime? date) => date.HasValue ? date.Value.ToString("HH:mm") : "-";
    public string DisplayValue(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value;
    public string DisplayMoney(decimal value) => value <= 0 ? "غير مدخل" : $"IQD {value:N0}";

    public string GetInitials(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "م";
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0][0].ToString();
        return $"{parts[0][0]}{parts[^1][0]}";
    }

    public string StatusText(string status)
    {
        if (status.Equals("Approved", StringComparison.OrdinalIgnoreCase)) return "موافق عليه";
        if (status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)) return "مرفوض";
        if (status.Equals("Pending", StringComparison.OrdinalIgnoreCase)) return "قيد الموافقة";
        if (status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)) return "ملغي";
        return string.IsNullOrWhiteSpace(status) ? "-" : status;
    }

    public string FeedbackStatusText(string status)
    {
        if (status.Equals("Open", StringComparison.OrdinalIgnoreCase)) return "مفتوحة";
        if (status.Equals("Pending", StringComparison.OrdinalIgnoreCase)) return "قيد المعالجة";
        if (status.Equals("Answered", StringComparison.OrdinalIgnoreCase)) return "تم الرد";
        if (status.Equals("Closed", StringComparison.OrdinalIgnoreCase)) return "مغلقة";
        return string.IsNullOrWhiteSpace(status) ? "-" : status;
    }

    public string StatusClass(string status)
    {
        if (status.Equals("Approved", StringComparison.OrdinalIgnoreCase) || status.Equals("Answered", StringComparison.OrdinalIgnoreCase)) return "live";
        if (status.Equals("Pending", StringComparison.OrdinalIgnoreCase) || status.Equals("Open", StringComparison.OrdinalIgnoreCase)) return "pending";
        if (status.Equals("Rejected", StringComparison.OrdinalIgnoreCase) || status.Equals("Closed", StringComparison.OrdinalIgnoreCase) || status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)) return "danger";
        return string.Empty;
    }

    public string RequestIcon(string requestType)
    {
        if (requestType.Contains("إجازة", StringComparison.OrdinalIgnoreCase)) return "🌴";
        if (requestType.Contains("بصمة", StringComparison.OrdinalIgnoreCase)) return "☝️";
        if (requestType.Contains("شخصي", StringComparison.OrdinalIgnoreCase)) return "🚶";
        if (requestType.Contains("عمل", StringComparison.OrdinalIgnoreCase)) return "💼";
        if (requestType.Contains("أوفر", StringComparison.OrdinalIgnoreCase)) return "⏱️";
        return "📝";
    }

    public string AnnouncementTheme(string category)
    {
        if (category.Contains("تهنئة", StringComparison.OrdinalIgnoreCase)) return "theme-congrats";
        if (category.Contains("تعزية", StringComparison.OrdinalIgnoreCase)) return "theme-condolence";
        if (category.Contains("تعليمات", StringComparison.OrdinalIgnoreCase)) return "theme-policy";
        if (category.Contains("عطلة", StringComparison.OrdinalIgnoreCase)) return "theme-holiday";
        return "theme-general";
    }

    private bool IsFinalStatus(string status) =>
        status.Equals("Approved", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("Rejected", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase);

    private string BuildServicePeriod(DateTime? hireDate)
    {
        if (!hireDate.HasValue) return "-";
        var start = hireDate.Value.Date;
        var today = DateTime.Today;
        if (start > today) return "-";
        var months = ((today.Year - start.Year) * 12) + today.Month - start.Month;
        if (today.Day < start.Day) months--;
        var years = Math.Max(0, months / 12);
        var restMonths = Math.Max(0, months % 12);
        if (years == 0 && restMonths == 0) return "أقل من شهر";
        if (years == 0) return $"{restMonths} شهر";
        if (restMonths == 0) return $"{years} سنة";
        return $"{years} سنة و {restMonths} شهر";
    }

    private string NormalizeTab(string? tab)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "home", "profile", "attendance", "compensation", "requests", "pulse", "feedback", "performance"
        };
        return !string.IsNullOrWhiteSpace(tab) && allowed.Contains(tab) ? tab : "home";
    }

    public record EmployeePortalEmployee
    {
        public static EmployeePortalEmployee Empty => new()
        {
            FullName = "موظف",
            EmployeeNo = "-",
            Position = "Employee",
            DepartmentName = "-",
            BranchName = "-"
        };

        public int Id { get; init; }
        public string EmployeeNo { get; init; } = string.Empty;
        public string FullName { get; init; } = string.Empty;
        public string NationalId { get; init; } = string.Empty;
        public string Phone { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string Position { get; init; } = string.Empty;
        public DateTime? HireDate { get; init; }
        public DateTime? BirthDate { get; init; }
        public bool IsActive { get; init; }
        public string DepartmentName { get; init; } = string.Empty;
        public string BranchName { get; init; } = string.Empty;
        public string ManagerName { get; init; } = string.Empty;
    }

    public class EmployeePortalCompensation
    {
        public decimal BasicSalary { get; set; }
        public decimal Allowances { get; set; }
        public decimal Deductions { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string BankAccount { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public decimal NetAmount => BasicSalary + Allowances - Deductions;
        public bool HasData => BasicSalary > 0 || Allowances > 0 || Deductions > 0 || !string.IsNullOrWhiteSpace(PaymentMethod);
    }

    public class EmployeePortalAnnouncement
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string TargetType { get; set; } = string.Empty;
        public string TargetValue { get; set; } = string.Empty;
        public DateTime? PublishDate { get; set; }
        public bool IsRead { get; set; }
        public DateTime? FirstReadAtUtc { get; set; }
    }

    public class EmployeePortalPoll
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Question { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string TargetType { get; set; } = string.Empty;
        public string TargetValue { get; set; } = string.Empty;
        public DateTime? PublishDate { get; set; }
        public bool HasVoted { get; set; }
        public List<EmployeePortalPollOption> Options { get; set; } = new();
    }

    public class EmployeePortalPollOption
    {
        public int Id { get; set; }
        public string OptionText { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
    }

    public class EmployeePortalRequest
    {
        public int Id { get; set; }
        public string RequestType { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class EmployeePortalAttendance
    {
        public DateTime? AttendanceDate { get; set; }
        public DateTime? CheckIn { get; set; }
        public DateTime? CheckOut { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    public class EmployeePortalTeamMember
    {
        public int Id { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
    }

    public class EmployeePortalFeedback
    {
        public int Id { get; set; }
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

    public class FeedbackInput
    {
        public string Type { get; set; } = "اقتراح";
        public string Priority { get; set; } = "متوسط";
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class SelfServiceRequestInput
    {
        public string RequestType { get; set; } = "إجازة";
        public DateTime? FromDate { get; set; } = DateTime.Today;
        public DateTime? ToDate { get; set; } = DateTime.Today;
        public string Reason { get; set; } = string.Empty;
    }

    public class PollVoteInput
    {
        public int PollId { get; set; }
        public int OptionId { get; set; }
    }
}

