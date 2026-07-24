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
    private readonly IWebHostEnvironment _environment;

    public IndexModel(
        ApplicationDbContext dbContext,
        IAnnouncementService announcementService,
        IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _announcementService = announcementService;
        _environment = environment;
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

    /// <summary>طلبات البصمة المفقودة التي قدّمها هذا الموظف (تصل لصفحة الإدارة).</summary>
    public List<MissingPunchRequestStore.Request> MyMissingPunches { get; private set; } = new();

    /// <summary>آخر بصمات الموظف عبر الإنترنت (تأكيد فوري للبصم الذاتي).</summary>
    public List<OnlinePunchStore.OnlinePunch> MyOnlinePunches { get; private set; } = new();

    /// <summary>الشهر المختار لعرض سجلات الحضور (yyyy-MM) — فارغ = الأحدث.</summary>
    [BindProperty(SupportsGet = true)]
    public string? AttMonth { get; set; }

    /// <summary>الأشهر المتاحة بسجلات الحضور (للمنتقي) — (القيمة yyyy-MM، التسمية العربية).</summary>
    public List<(string Value, string Label)> AttendanceMonths { get; private set; } = new();

    /// <summary>أوقات بصم اليوم (ISO) — لعدّاد ساعات العمل الحيّ من أول بصمة.</summary>
    public List<string> TodayPunchIso { get; private set; } = new();

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

        var requestId = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            """
INSERT INTO SelfServiceRequests
(EmployeeId, RequestType, CreatedAt, FromDate, ToDate, Reason, Status)
VALUES
(@EmployeeId, @RequestType, SYSUTCDATETIME(), @FromDate, @ToDate, @Reason, 'Pending');
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@RequestType", type);
                HrmsDatabase.AddParameter(command, "@FromDate", fromDate.Value);
                HrmsDatabase.AddParameter(command, "@ToDate", toDate);
                HrmsDatabase.AddParameter(command, "@Reason", reason);
            });

        // سريان الموافقات: حلّ القالب وتجميد خطوات اللجنة على الطلب.
        if (requestId > 0)
        {
            await ApprovalWorkflowEngine.StartAsync(_dbContext, requestId, type, employeeId);
        }

        StatusMessage = $"تم إرسال طلب {type} بنجاح وهو الآن قيد المراجعة.";
        return RedirectToPage(new { tab = returnTab ?? "requests" });
    }

    /// <summary>
    /// طلب إجازة مُهيكل من الشاشة المنبثقة (المرحلة 1: سنوية/مرضية). يتحقّق من
    /// رصيد السنوية، يحسب عدد الأيام، يحفظ مرفق صورة اختياري، ثم يبدأ سريان الموافقات.
    /// </summary>
    public async Task<IActionResult> OnPostCreateLeaveAsync(
        string? reqType,
        DateTime? from,
        DateTime? to,
        string? fromTime,
        string? toTime,
        string? reason,
        IFormFile? attachment)
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            employeeId = await HrmsDatabase.ScalarAsync<int>(
                _dbContext, "SELECT TOP 1 Id FROM Employees ORDER BY Id");
        }
        if (employeeId <= 0)
        {
            StatusMessage = "تعذّر تحديد الموظف.";
            return RedirectToPage(new { tab = "requests" });
        }

        var typeLabel = (reqType ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(typeLabel))
        {
            StatusMessage = "يرجى اختيار نوع الطلب.";
            return RedirectToPage(new { tab = "requests" });
        }
        if (!from.HasValue || !to.HasValue)
        {
            StatusMessage = "يرجى تحديد تاريخي البداية والنهاية.";
            return RedirectToPage(new { tab = "requests" });
        }
        if (to.Value.Date < from.Value.Date)
        {
            StatusMessage = "تاريخ النهاية لا يمكن أن يكون قبل تاريخ البداية.";
            return RedirectToPage(new { tab = "requests" });
        }

        // ضوابط النوع من المتجر الداينمك.
        var typeDef = (await RequestTypeStore.ListTypesAsync(_dbContext, onlyActive: true))
            .FirstOrDefault(x => x.Name == typeLabel);
        if (typeDef is { NeedsTime: true } &&
            (string.IsNullOrWhiteSpace(fromTime) || string.IsNullOrWhiteSpace(toTime)))
        {
            StatusMessage = "يرجى تحديد وقت البداية والنهاية.";
            return RedirectToPage(new { tab = "requests" });
        }
        if (typeDef is { AttachmentRequired: true } && attachment is not { Length: > 0 })
        {
            StatusMessage = $"المرفق إلزامي لهذا النوع{(string.IsNullOrWhiteSpace(typeDef.AttachmentLabel) ? "" : $" ({typeDef.AttachmentLabel})")}.";
            return RedirectToPage(new { tab = "requests" });
        }

        // تقاطع منتصف الليل للأنواع الزمنية (أوفرتايم/مغادرة متأخرة): النهاية في اليوم التالي.
        if (typeDef is { NeedsTime: true } &&
            TimeSpan.TryParse(fromTime, out var stTs) && TimeSpan.TryParse(toTime, out var etTs) &&
            etTs <= stTs)
        {
            to = from.Value.AddDays(1);
        }

        var days = (decimal)((to.Value.Date - from.Value.Date).Days + 1);

        if (typeDef?.AllowedDays is int maxDays && days > maxDays)
        {
            StatusMessage = $"عدد الأيام يتجاوز المسموح ({maxDays} يوم) لهذا النوع.";
            return RedirectToPage(new { tab = "requests" });
        }

        // بوابة الرصيد للإجازة السنوية.
        if (typeLabel == "إجازة سنوية")
        {
            var balances = await LeaveBalanceCalculator.ForEmployeeAsync(
                _dbContext, employeeId, from.Value.Year);
            var annual = balances.FirstOrDefault(
                b => b.Type == SmartAttendance.Domain.Enums.LeaveType.Annual);
            var remaining = annual?.Remaining ?? 0m;
            if (days > remaining)
            {
                StatusMessage = $"رصيد الإجازة السنوية غير كافٍ (المتبقّي {remaining:0.#} يوم، والمطلوب {days:0.#}).";
                return RedirectToPage(new { tab = "requests" });
            }
        }

        // حفظ المرفق (صورة اختيارية).
        string? attachmentPath = null;
        if (attachment is { Length: > 0 })
        {
            var ext = Path.GetExtension(attachment.FileName);
            var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".pdf" };
            if (allowed.Contains(ext.ToLowerInvariant()))
            {
                var dir = Path.Combine(_environment.WebRootPath, "uploads", "requests");
                Directory.CreateDirectory(dir);
                var fileName = $"req_{employeeId}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
                await using (var stream = System.IO.File.Create(Path.Combine(dir, fileName)))
                {
                    await attachment.CopyToAsync(stream);
                }
                attachmentPath = $"/uploads/requests/{fileName}";
            }
        }

        reason = string.IsNullOrWhiteSpace(reason) ? "تم الإرسال من بوابة الموظف" : reason.Trim();

        // وقت اختياري (للمغادرات): "HH:mm".
        TimeSpan? startTs = TimeSpan.TryParse(fromTime, out var st) ? st : null;
        TimeSpan? endTs = TimeSpan.TryParse(toTime, out var et) ? et : null;

        var requestId = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            """
INSERT INTO SelfServiceRequests
(EmployeeId, RequestType, CreatedAt, FromDate, ToDate, StartTime, EndTime, Reason, Status, DaysCount, AttachmentPath)
VALUES
(@EmployeeId, @RequestType, SYSUTCDATETIME(), @FromDate, @ToDate, @StartTime, @EndTime, @Reason, 'Pending', @DaysCount, @AttachmentPath);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@RequestType", typeLabel);
                HrmsDatabase.AddParameter(command, "@FromDate", from.Value);
                HrmsDatabase.AddParameter(command, "@ToDate", to.Value);
                HrmsDatabase.AddParameter(command, "@StartTime", (object?)startTs ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@EndTime", (object?)endTs ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Reason", reason);
                HrmsDatabase.AddParameter(command, "@DaysCount", days);
                HrmsDatabase.AddParameter(command, "@AttachmentPath", (object?)attachmentPath ?? DBNull.Value);
            });

        if (requestId > 0)
        {
            await ApprovalWorkflowEngine.StartAsync(_dbContext, requestId, typeLabel, employeeId);
        }

        StatusMessage = $"تم إرسال {typeLabel} ({days:0.#} يوم) وهو الآن قيد المراجعة.";
        return RedirectToPage(new { tab = "requests" });
    }

    /// <summary>
    /// تقديم طلب بصمة مفقودة مُهيكل من الموظف (نمط كيان — حلقة الخدمة الذاتية):
    /// يصل لصفحة إدارة طلبات البصمة بمصدر «خدمة ذاتية» بحالة «قيد الانتظار» ليبتّ
    /// فيه المسؤول. عند الموافقة تُنشأ البصمة الفعلية.
    /// </summary>
    public async Task<IActionResult> OnPostSubmitMissingPunchAsync(string? returnTab)
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            // نفس fallback العرض: مستخدم غير مربوط بموظف (وضع تجريبي) ← أول موظف.
            employeeId = await HrmsDatabase.ScalarAsync<int>(_dbContext, "SELECT TOP 1 Id FROM Employees ORDER BY Id");
        }
        if (employeeId <= 0)
        {
            StatusMessage = "لا يمكن إرسال الطلب لأن المستخدم غير مرتبط بموظف.";
            return RedirectToPage(new { tab = returnTab ?? "requests" });
        }

        var form = Request.Form;
        DateTime? punchAt = null;
        if (DateOnly.TryParse(form["MpDate"], out var d) && TimeOnly.TryParse(form["MpTime"], out var tm))
            punchAt = d.ToDateTime(tm);

        if (punchAt == null)
        {
            StatusMessage = "يرجى إدخال تاريخ ووقت البصمة المفقودة.";
            return RedirectToPage(new { tab = returnTab ?? "requests" });
        }

        // النوع (دخول/خروج) يُشتَق تلقائياً بالأسبقية الزمنية بين بصمات اليوم الموجودة
        // والبصمة المُضافة — لا اختيار يدوي (نمط كيان: الترتيب يحدّد الدلالة).
        var existingTimes = await PunchTypingEngine.DayPunchTimesAsync(
            _dbContext, employeeId, DateOnly.FromDateTime(punchAt.Value));
        var derivedType = PunchTypingEngine.DeriveTypeFor(existingTimes, punchAt.Value);

        var (ok, message) = await MissingPunchRequestStore.SaveAsync(_dbContext, new MissingPunchRequestStore.Request
        {
            EmployeeId = employeeId,
            PunchAt = punchAt.Value,
            PunchType = derivedType,
            Reason = string.IsNullOrWhiteSpace(form["MpReason"]) ? null : form["MpReason"].ToString().Trim(),
            Source = "خدمة ذاتية"
        }, User.Identity?.Name ?? "employee");

        StatusMessage = ok ? "تم إرسال طلب البصمة المفقودة وهو الآن قيد مراجعة الموارد البشرية." : message;
        return RedirectToPage(new { tab = returnTab ?? "requests" });
    }

    /// <summary>
    /// بصمات يوم الموظف (AJAX) — للمعاينة الحيّة بنموذج نسيان البصمة: يُرجِع أوقات البصم
    /// الموجودة مصنَّفةً بالأسبقية (دخول/خروج) ليدمج المتصفح الوقت المُدخَل ويُعيد التصنيف.
    /// </summary>
    public async Task<IActionResult> OnGetDayPunchesAsync(string? date)
    {
        if (!DateOnly.TryParse(date, out var d))
            return new JsonResult(new { punches = Array.Empty<object>() });

        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
            employeeId = await HrmsDatabase.ScalarAsync<int>(_dbContext, "SELECT TOP 1 Id FROM Employees ORDER BY Id");
        if (employeeId <= 0)
            return new JsonResult(new { punches = Array.Empty<object>() });

        var times = await PunchTypingEngine.DayPunchTimesAsync(_dbContext, employeeId, d);
        var typed = PunchTypingEngine.Derive(times);
        return new JsonResult(new
        {
            punches = typed.Select(p => new { at = p.At.ToString("HH:mm"), type = p.Type }).ToArray()
        });
    }

    /// <summary>
    /// طلب «تعديل بياناتي»: يجمع الحقول التي غيّرها الموظف (new_&lt;key&gt;) ويقارنها
    /// بالقيم الحالية، فيُنشئ طلب خدمة ذاتية يمرّ عبر لجنة الموافقة. لا يُطبَّق على
    /// الملف إلا بعد الاعتماد النهائي (خلاف التعديل المباشر بشاشة «ملفي»).
    /// </summary>
    public async Task<IActionResult> OnPostSubmitDataChangeAsync(string? returnTab)
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
            employeeId = await HrmsDatabase.ScalarAsync<int>(_dbContext, "SELECT TOP 1 Id FROM Employees ORDER BY Id");
        if (employeeId <= 0)
        {
            StatusMessage = "لا يمكن إرسال الطلب لأن المستخدم غير مرتبط بموظف.";
            return RedirectToPage(new { tab = returnTab ?? "requests" });
        }

        await DataChangeRequestStore.EnsureAsync(_dbContext);
        var editable = await DataChangeRequestStore.ListEditableAsync(_dbContext, employeeId);

        var proposed = new List<DataChangeRequestStore.ProposedField>();
        foreach (var f in editable)
        {
            var raw = Request.Form[$"new_{f.Key}"].ToString();
            if (string.IsNullOrWhiteSpace(raw)) continue;
            proposed.Add(new DataChangeRequestStore.ProposedField
            {
                Key = f.Key,
                OldValue = f.OldValue,
                NewValue = raw.Trim()
            });
        }

        if (proposed.Count == 0)
        {
            StatusMessage = "لم تُدخِل أي قيمة جديدة لتعديلها.";
            return RedirectToPage(new { tab = returnTab ?? "requests" });
        }

        var reason = Request.Form["dcReason"].ToString();
        reason = string.IsNullOrWhiteSpace(reason) ? "طلب تعديل بيانات من بوابة الموظف" : reason.Trim();

        var requestId = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            """
INSERT INTO SelfServiceRequests (EmployeeId, RequestType, CreatedAt, Reason, Status)
VALUES (@Emp, @Type, SYSUTCDATETIME(), @Reason, 'Pending');
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Emp", employeeId);
                HrmsDatabase.AddParameter(command, "@Type", DataChangeRequestStore.RequestTypeLabel);
                HrmsDatabase.AddParameter(command, "@Reason", reason);
            });

        var saved = requestId > 0 ? await DataChangeRequestStore.SaveFieldsAsync(_dbContext, requestId, proposed) : 0;
        if (saved == 0)
        {
            if (requestId > 0)
                await HrmsDatabase.ExecuteAsync(_dbContext, "DELETE FROM SelfServiceRequests WHERE Id=@r",
                    cmd => HrmsDatabase.AddParameter(cmd, "@r", requestId));
            StatusMessage = "لم تُدخِل أي قيمة مختلفة عن الحالية.";
            return RedirectToPage(new { tab = returnTab ?? "requests" });
        }

        await ApprovalWorkflowEngine.StartAsync(_dbContext, requestId, DataChangeRequestStore.RequestTypeLabel, employeeId);
        StatusMessage = $"تم إرسال طلب تعديل البيانات ({saved} حقل) وهو الآن قيد المراجعة.";
        return RedirectToPage(new { tab = returnTab ?? "requests" });
    }

    /// <summary>
    /// البصمة عبر الإنترنت (بصم ذاتي من البوابة — نمط كيان قسم 36.ج): يسجّل بصمة
    /// دخول/خروج بوقت الخادم الحالي بمصدر «موبايل»، فتدخل اشتقاق اليومية كأي بصمة.
    /// </summary>
    public async Task<IActionResult> OnPostOnlinePunchAsync(string? punchType, string? returnTab)
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
            employeeId = await HrmsDatabase.ScalarAsync<int>(_dbContext, "SELECT TOP 1 Id FROM Employees ORDER BY Id");
        if (employeeId <= 0)
        {
            StatusMessage = "لا يمكن تسجيل البصمة لأن المستخدم غير مرتبط بموظف.";
            return RedirectToPage(new { tab = returnTab ?? "attendance" });
        }

        var type = punchType == "Out" ? "Out" : "In";
        var now = DateTime.Now;
        var recordId = await OnlinePunchStore.RecordAsync(_dbContext, employeeId, type, now, null);

        StatusMessage = recordId > 0
            ? $"سُجّلت بصمة {(type == "Out" ? "الانصراف" : "الحضور")} عبر الإنترنت الساعة {now:HH:mm} — تدخل الحضور عند «تحديث الحضور»."
            : "تم تجاهل البصمة: سُجّلت بصمة مماثلة خلال أقل من دقيقة.";
        return RedirectToPage(new { tab = returnTab ?? "attendance" });
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
        // افتراضياً يُعرض الشهر الحالي (null = لم يُختَر بعد؛ "" = «أحدث السجلات» يدوياً).
        AttMonth ??= DateTime.Today.ToString("yyyy-MM");
        Attendance = await LoadAttendanceAsync(employeeId);
        AttendanceMonths = await LoadAttendanceMonthsAsync(employeeId);
        // ملء أيام الغياب: كل يوم بالشهر المختار (حتى اليوم) بلا بصمة = غياب (والجمعة/السبت عطلة).
        if (TryParseMonth(AttMonth, out var mStart, out var mEnd))
        {
            var have = Attendance.Where(a => a.AttendanceDate.HasValue)
                                 .Select(a => a.AttendanceDate!.Value.Date).ToHashSet();
            var last = mEnd.AddDays(-1);
            if (last > DateTime.Today) last = DateTime.Today;
            var fill = new List<EmployeePortalAttendance>();
            for (var d = mStart; d <= last; d = d.AddDays(1))
            {
                if (have.Contains(d.Date)) continue;
                var weekend = d.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;
                fill.Add(new EmployeePortalAttendance { AttendanceDate = d, Status = weekend ? "weekend" : "absence" });
            }
            Attendance = Attendance.Concat(fill).OrderByDescending(a => a.AttendanceDate).ToList();
        }
        // أوقات بصم اليوم لعدّاد ساعات العمل الحيّ.
        try
        {
            var todayTimes = await PunchTypingEngine.DayPunchTimesAsync(
                _dbContext, employeeId, DateOnly.FromDateTime(DateTime.Today));
            TodayPunchIso = todayTimes.Select(t => t.ToString("yyyy-MM-ddTHH:mm:ss")).ToList();
        }
        catch { TodayPunchIso = new(); }
        Team = await LoadTeamAsync(Employee);
        FeedbackItems = await LoadFeedbackAsync(employeeId);
        PendingViolationReplies = await LoadPendingViolationRepliesAsync(employeeId);
        PendingAssetAcknowledgments = await LoadPendingAssetAcknowledgmentsAsync(employeeId);
        MyMissingPunches = await MissingPunchRequestStore.ListAsync(
            _dbContext, new MissingPunchRequestStore.Filter { EmployeeId = employeeId });
        MyOnlinePunches = await OnlinePunchStore.ListAsync(
            _dbContext, new OnlinePunchStore.Filter { EmployeeId = employeeId, Top = 200 });

        // رصيد الإجازات للوحة «إجراءاتي» — لوحة اختيارية لا تُسقط البوابة عند تعذّرها.
        try
        {
            LeaveBalances = await LeaveBalanceCalculator.ForEmployeeAsync(
                _dbContext, employeeId, DateTime.Now.Year);
        }
        catch
        {
            LeaveBalances = new();
        }

        // أنواع الطلبات الداينمك (تبويبات + أنواع بضوابطها) لشاشة الإجازة.
        try
        {
            await RequestTypeStore.EnsureAsync(_dbContext);
            ReqCategories = await RequestTypeStore.ListCategoriesAsync(_dbContext, onlyActive: true);
            ReqTypes = await RequestTypeStore.ListTypesAsync(_dbContext, onlyActive: true);
        }
        catch
        {
            ReqCategories = new();
            ReqTypes = new();
        }

        // حقول «تعديل بياناتي» + طلبات التعديل المعلّقة (لعرضها بتبويب الطلبات مع تعديل/حذف).
        try
        {
            await DataChangeRequestStore.EnsureAsync(_dbContext);
            DataChangeFields = await DataChangeRequestStore.ListEditableAsync(_dbContext, employeeId);
            PendingDataChanges = await DataChangeRequestStore.ListPendingForEmployeeAsync(_dbContext, employeeId);
        }
        catch
        {
            DataChangeFields = new();
            PendingDataChanges = new();
        }
    }

    /// <summary>حذف طلب تعديل بيانات معلّق من تبويب الطلبات (لصاحبه فقط، قبل الاعتماد).</summary>
    public async Task<IActionResult> OnPostDeleteDataChangeAsync(int id)
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
            employeeId = await HrmsDatabase.ScalarAsync<int>(_dbContext, "SELECT TOP 1 Id FROM Employees ORDER BY Id");
        var ok = await DataChangeRequestStore.DeletePendingRequestAsync(_dbContext, id, employeeId);
        StatusMessage = ok ? "تم حذف طلب التعديل المعلّق." : "تعذّر الحذف (الطلب غير موجود أو تمّ البتّ فيه).";
        return RedirectToPage(new { tab = "requests" });
    }

    public List<LeaveBalanceCalculator.TypeBalance> LeaveBalances { get; set; } = new();
    public List<RequestTypeStore.Category> ReqCategories { get; set; } = new();
    public List<RequestTypeStore.ReqType> ReqTypes { get; set; } = new();
    public List<DataChangeRequestStore.ProposedField> DataChangeFields { get; set; } = new();
    public List<DataChangeRequestStore.PendingRequest> PendingDataChanges { get; set; } = new();

    public static string LeaveTypeArabic(SmartAttendance.Domain.Enums.LeaveType type) => type switch
    {
        SmartAttendance.Domain.Enums.LeaveType.Annual => "سنوية",
        SmartAttendance.Domain.Enums.LeaveType.Sick => "مرضية",
        SmartAttendance.Domain.Enums.LeaveType.Emergency => "طارئة",
        SmartAttendance.Domain.Enums.LeaveType.Unpaid => "بدون راتب",
        SmartAttendance.Domain.Enums.LeaveType.Official => "رسمية",
        _ => type.ToString()
    };

    public sealed class PendingAssetAcknowledgment
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public DateOnly? FromDate { get; set; }
        public decimal? Amount { get; set; }
    }

    public List<PendingAssetAcknowledgment> PendingAssetAcknowledgments { get; set; } = new();

    private async Task<List<PendingAssetAcknowledgment>> LoadPendingAssetAcknowledgmentsAsync(int employeeId)
    {
        await EmployeeRecordsSchema.EnsureAsync(_dbContext);
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT Id, Title, Subtitle, FromDate, Amount
FROM EmployeeFileRecords
WHERE EmployeeId = @EmployeeId
  AND RecordType = @AssetType
  AND ISNULL(IsReturned, 0) = 0
  AND ISNULL(EmployeeAcknowledged, 0) = 0
ORDER BY Id DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@AssetType", (int)SmartAttendance.Domain.Enums.EmployeeRecordType.Asset);
            },
            reader => new PendingAssetAcknowledgment
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Title = HrmsDatabase.GetString(reader, "Title"),
                Subtitle = HrmsDatabase.GetString(reader, "Subtitle"),
                FromDate = HrmsDatabase.GetDateOnly(reader, "FromDate"),
                Amount = reader["Amount"] as decimal?
            });
    }

    /// <summary>إقرار الموظف باستلام العهدة (نمط كيان «موافقة الموظف») — يظهر بإدارة العهد.</summary>
    public async Task<IActionResult> OnPostAcknowledgeAssetAsync(int id, string? returnTab)
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            employeeId = await HrmsDatabase.ScalarAsync<int>(_dbContext, "SELECT TOP 1 Id FROM Employees ORDER BY Id");
        }

        await EmployeeRecordsSchema.EnsureAsync(_dbContext);
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
UPDATE EmployeeFileRecords
SET EmployeeAcknowledged = 1, AcknowledgedAt = SYSUTCDATETIME(), UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id AND EmployeeId = @EmployeeId
  AND RecordType = @AssetType AND ISNULL(EmployeeAcknowledged, 0) = 0;

INSERT INTO SystemNotifications (Title, Message, TargetRole, Url)
SELECT N'إقرار استلام عهدة',
       N'أقرّ الموظف باستلام العهدة: ' + r.Title,
       'HR', '/AssetsManagement'
FROM EmployeeFileRecords r WHERE r.Id = @Id AND r.EmployeeId = @EmployeeId;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@AssetType", (int)SmartAttendance.Domain.Enums.EmployeeRecordType.Asset);
            });

        StatusMessage = "تم تسجيل إقرارك باستلام العهدة.";
        return RedirectToPage(new { tab = returnTab ?? "requests" });
    }

    public sealed class PendingViolationReply
    {
        public int Id { get; set; }
        public string ReferenceNo { get; set; } = string.Empty;
        public string ViolationTitle { get; set; } = string.Empty;
        public DateOnly? EventDate { get; set; }
    }

    public List<PendingViolationReply> PendingViolationReplies { get; set; } = new();

    private async Task<List<PendingViolationReply>> LoadPendingViolationRepliesAsync(int employeeId)
    {
        await ViolationCaseSchema.EnsureAsync(_dbContext);
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT Id, ReferenceNo, ISNULL(ViolationTitle, N'') AS ViolationTitle, EventDate
FROM EmployeeViolationCases
WHERE EmployeeId = @EmployeeId AND ISNULL(IsDeleted, 0) = 0
  AND ISNULL(EmployeeReplyStatus, N'NotRequested') = N'Pending'
ORDER BY EventDate DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new PendingViolationReply
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                ReferenceNo = HrmsDatabase.GetString(reader, "ReferenceNo"),
                ViolationTitle = HrmsDatabase.GetString(reader, "ViolationTitle"),
                EventDate = HrmsDatabase.GetDateOnly(reader, "EventDate")
            });
    }

    /// <summary>ردّ الموظف على مخالفة (حق الدفاع) — يُحفظ نصاً ويُشعر HR.</summary>
    public async Task<IActionResult> OnPostViolationReplyAsync(int id, string reply, string? returnTab)
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            // نفس fallback الوضع التجريبي المستخدم بعرض البوابة (مستخدم غير مربوط بموظف).
            employeeId = await HrmsDatabase.ScalarAsync<int>(_dbContext, "SELECT TOP 1 Id FROM Employees ORDER BY Id");
        }

        if (employeeId <= 0 || string.IsNullOrWhiteSpace(reply))
        {
            StatusMessage = "يرجى كتابة نص الرد.";
            return RedirectToPage(new { tab = returnTab ?? "requests" });
        }

        await ViolationCaseSchema.EnsureAsync(_dbContext);
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
UPDATE EmployeeViolationCases
SET EmployeeReply = @Reply, EmployeeReplyStatus = N'Replied',
    EmployeeRepliedAt = SYSUTCDATETIME(), UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id AND EmployeeId = @EmployeeId
  AND ISNULL(EmployeeReplyStatus, N'NotRequested') = N'Pending';

INSERT INTO SystemNotifications (Title, Message, TargetRole, Url)
SELECT N'رد موظف على مخالفة',
       N'ردّ الموظف على المخالفة ' + v.ReferenceNo + N' — راجع الرد بسجل المخالفات.',
       'HR', '/Violations?Reply=replied'
FROM EmployeeViolationCases v WHERE v.Id = @Id AND v.EmployeeId = @EmployeeId;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Reply", reply.Trim());
            });

        StatusMessage = "تم إرسال ردّك على المخالفة للموارد البشرية.";
        return RedirectToPage(new { tab = returnTab ?? "requests" });
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
    ISNULL(e.PhotoPath, '') AS PhotoPath,
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
                PhotoPath = HrmsDatabase.GetString(reader, "PhotoPath"),
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
        // شهر محدَّد (yyyy-MM) ⟶ كل سجلات ذلك الشهر؛ وإلا أحدث 30 سجلاً.
        var hasMonth = TryParseMonth(AttMonth, out var monthStart, out var monthEnd);
        // تجميع صارم: صف واحد لكل يوم (أول دخول = أدنى وقت، آخر خروج = أعلى وقت) — يمنع
        // تكرار اليوم حين تتعدّد بصمات نفس التاريخ. آخر نشاط = MAX(CheckOut المُشتَق).
        var sql = hasMonth
            ? """
SELECT AttendanceDate,
       MIN(CheckIn) AS CheckIn,
       MAX(CASE WHEN CheckOut IS NOT NULL THEN CheckOut ELSE CheckIn END) AS CheckOut,
       CAST(COUNT(*) AS nvarchar(50)) AS Status,
       '' AS Source, '' AS Notes
FROM AttendanceRecords
WHERE EmployeeId = @EmployeeId AND ISNULL(IsDeleted,0) = 0
      AND AttendanceDate >= @From AND AttendanceDate < @To
GROUP BY AttendanceDate
ORDER BY AttendanceDate DESC;
"""
            : """
SELECT TOP 30 AttendanceDate, CheckIn, CheckOut, Status, Source, Notes FROM (
    SELECT AttendanceDate,
           MIN(CheckIn) AS CheckIn,
           MAX(CheckOut) AS CheckOut,
           CAST(COUNT(*) AS nvarchar(50)) AS Status,
           '' AS Source, '' AS Notes
    FROM AttendanceRecords
    WHERE EmployeeId = @EmployeeId AND ISNULL(IsDeleted,0) = 0
    GROUP BY AttendanceDate
) d
ORDER BY AttendanceDate DESC;
""";

        return await HrmsDatabase.QueryAsync(
            _dbContext,
            sql,
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                if (hasMonth)
                {
                    HrmsDatabase.AddParameter(command, "@From", monthStart);
                    HrmsDatabase.AddParameter(command, "@To", monthEnd);
                }
            },
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

    /// <summary>الأشهر المتوفّرة بسجلات الحضور (أحدث 12) للمنتقي.</summary>
    private async Task<List<(string, string)>> LoadAttendanceMonthsAsync(int employeeId)
    {
        var arMonths = new[] { "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو",
                               "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT DISTINCT TOP 12 YEAR(AttendanceDate) AS Y, MONTH(AttendanceDate) AS M
FROM AttendanceRecords WHERE EmployeeId = @EmployeeId
ORDER BY Y DESC, M DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => (Y: HrmsDatabase.GetInt(reader, "Y"), M: HrmsDatabase.GetInt(reader, "M")));

        return rows.Select(r => ($"{r.Y:0000}-{r.M:00}", $"{arMonths[r.M - 1]} {r.Y}")).ToList();
    }

    private static bool TryParseMonth(string? value, out DateTime start, out DateTime end)
    {
        start = default; end = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('-');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var y) || !int.TryParse(parts[1], out var m)
            || m < 1 || m > 12) return false;
        start = new DateTime(y, m, 1);
        end = start.AddMonths(1);
        return true;
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
        public string PhotoPath { get; init; } = string.Empty;
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

