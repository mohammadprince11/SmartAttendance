using System.Security.Claims;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Application.Announcements.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Localization;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.EmployeePortal;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAnnouncementService _announcementService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILocalizationDictionaryService _localizationDictionary;

    private readonly Web.Infrastructure.Security.IProtectedFileService _protectedFiles;

    public IndexModel(
        ApplicationDbContext dbContext,
        IAnnouncementService announcementService,
        IWebHostEnvironment environment,
        ILocalizationDictionaryService localizationDictionary,
        Web.Infrastructure.Security.IProtectedFileService protectedFiles)
    {
        _dbContext = dbContext;
        _announcementService = announcementService;
        _environment = environment;
        _localizationDictionary = localizationDictionary;
        _protectedFiles = protectedFiles;
    }

    public string Tab { get; private set; } = "home";
    public int? RequestId { get; private set; }
    public EmployeePortalEmployee Employee { get; private set; } = EmployeePortalEmployee.Empty;
    public EmployeePortalCompensation Compensation { get; private set; } = new();
    public List<EmployeePortalAnnouncement> Announcements { get; private set; } = new();
    public List<EmployeePortalPoll> Polls { get; private set; } = new();
    public List<EmployeePortalRequest> Requests { get; private set; } = new();
    public List<EmployeePortalAttendance> Attendance { get; private set; } = new();
    public List<EmployeePortalTeamMember> Team { get; private set; } = new();
    public List<EmployeePortalFeedback> FeedbackItems { get; private set; } = new();
    public EmployeePortalFullProfile FullProfile { get; private set; } = new();

    /// <summary>طلبات البصمة المفقودة التي قدّمها هذا الموظف (تصل لصفحة الإدارة).</summary>
    public List<MissingPunchRequestStore.Request> MyMissingPunches { get; private set; } = new();

    /// <summary>آخر بصمات الموظف عبر الإنترنت (تأكيد فوري للبصم الذاتي).</summary>
    public List<OnlinePunchStore.OnlinePunch> MyOnlinePunches { get; private set; } = new();

    /// <summary>
    /// هل بصمة هذا الموظف تتطلب تأكيداً بيولوجياً؟ (راية الإعدادات مفعّلة + لديه مفتاح
    /// WebAuthn نشط معتمد) — تُشعِل اعتراض الإرسال بالواجهة لطلب بصمة/وجه الجهاز.
    /// </summary>
    public bool RequireBiometricPunch { get; private set; }

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

    [TempData(Key = "EmployeePortal.Index.StatusMessage")]
    public string? StatusMessage { get; set; }

    /// <summary>
    /// خطأ يخصّ محاولة الطلب الحالية فقط. لا يُحفظ في TempData كي لا ينتقل إلى
    /// صفحات أو تبويبات أخرى.
    /// </summary>
    public string? InlineRequestError { get; private set; }

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

    public async Task<IActionResult> OnGetAsync(string? tab, string? punch, int? pminm, int? prem, int? requestId)
    {
        RequestId = requestId is > 0 ? requestId : null;
        Tab = NormalizeTab(tab);
        await LoadAsync();

        if (RequestId.HasValue)
        {
            if (Requests.Any(request => request.Id == RequestId.Value))
                Tab = "requests";
            else
                RequestId = null;
        }

        var punchMessage = punch switch
        {
            "in" => "سُجّلت بصمة الحضور عبر الإنترنت — تدخل الحضور عند «تحديث الحضور».",
            "out" => "سُجّلت بصمة الانصراف عبر الإنترنت — تدخل الحضور عند «تحديث الحضور».",
            "toosoon" => $"لا يمكن تسجيل الانصراف قبل مرور {OnlinePunchStore.FormatDuration((pminm ?? 0) / 60d)} من تسجيل الحضور — تبقّى {OnlinePunchStore.FormatDuration((prem ?? 0) / 60d)}.",
            "dup" => "تم تجاهل البصمة: سُجّلت بصمة مماثلة خلال أقل من دقيقة.",
            "geo" => "رُفضت البصمة: أنت خارج نطاق موقع العمل المحدد لك (أو لم يصل موقعك — تأكد من السماح بالوصول للموقع).",
            "bio" => "رُفضت البصمة: مطلوب تأكيد بيولوجي (بصمة/وجه الجهاز) — أعد المحاولة واقبل طلب البصمة عند ظهوره.",
            _ => null
        };
        // لا نعيد إسناد رسالة TempData المقروءة لنفسها؛ ذلك كان يحفظها من جديد
        // ويجعل التنبيه عالقاً عند التنقّل بين كل صفحات البوابة.
        if (punchMessage is not null)
            StatusMessage = punchMessage;
        return Page();
    }

    /// <summary>فحص خفيف للواجهة قبل فتح أي نموذج طلب؛ الإنفاذ الحاسم يبقى في POST.</summary>
    public async Task<IActionResult> OnGetRequestEligibilityAsync()
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        var employeeId = await ResolveEmployeeIdAsync();
        var eligibility = await EmployeeRequestEligibility.CheckAsync(
            _dbContext,
            employeeId,
            HttpContext.RequestAborted);

        return new JsonResult(new
        {
            eligible = eligibility.IsEligible,
            message = eligibility.Message,
            missingFields = eligibility.MissingFields
        });
    }

    /// <summary>
    /// معاينة أثر طلب الإجازة قبل الإرسال. الحساب لا يُعاد في الواجهة؛ بل يمر من
    /// CompanyLeavePolicyStore نفسه حتى تُحترم الوردية والعطل والوحدة وسياسة الشركة.
    /// </summary>
    public async Task<IActionResult> OnGetRequestImpactAsync(
        string? reqType,
        string? from,
        string? to,
        string? fromTime,
        string? toTime,
        string? reason,
        bool hasAttachment = false)
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
            return new JsonResult(new { available = false, message = "تعذّر تحديد الموظف." });

        if (string.IsNullOrWhiteSpace(reqType) || !DateOnly.TryParse(from, out var fromDate))
            return new JsonResult(new { available = false });
        if (!DateOnly.TryParse(to, out var toDate)) toDate = fromDate;
        if (toDate < fromDate)
            return new JsonResult(new { available = false, valid = false, message = "تاريخ النهاية لا يمكن أن يكون قبل تاريخ البداية." });

        await RequestTypeStore.EnsureAsync(_dbContext);
        var type = (await RequestTypeStore.ListTypesAsync(_dbContext, onlyActive: true))
            .FirstOrDefault(item => string.Equals(item.Name, reqType.Trim(), StringComparison.OrdinalIgnoreCase));
        if (type is null)
            return new JsonResult(new { available = false, valid = false, message = "نوع الطلب غير متاح أو غير مفعّل." });

        TimeSpan? startTime = TimeSpan.TryParse(fromTime, out var parsedStart) ? parsedStart : null;
        TimeSpan? endTime = TimeSpan.TryParse(toTime, out var parsedEnd) ? parsedEnd : null;
        if (type.NeedsTime && startTime.HasValue && endTime.HasValue && endTime.Value <= startTime.Value)
            toDate = fromDate.AddDays(1);

        var companyId = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT ISNULL(CompanyId,0) FROM Employees WHERE Id=@EmployeeId AND ISNULL(IsDeleted,0)=0;",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId));
        var policy = companyId > 0
            ? (await CompanyLeavePolicyStore.ListForCompanyAsync(_dbContext, companyId, onlyActive: true))
                .FirstOrDefault(item => item.RequestTypeId == type.Id)
            : null;

        if (policy?.RequiresBalance != true)
            return new JsonResult(new
            {
                available = true,
                requiresBalance = false,
                valid = true,
                message = string.Empty
            });

        var validation = await CompanyLeavePolicyStore.ValidateRequestAsync(
            _dbContext,
            employeeId,
            currentRequestId: 0,
            requestTypeId: type.Id,
            requestTypeName: type.Name ?? reqType,
            fromDate,
            toDate,
            startTime,
            endTime,
            reason,
            hasAttachment);

        return new JsonResult(new
        {
            available = true,
            requiresBalance = true,
            valid = validation.Ok,
            message = validation.Message,
            entitlement = validation.Entitlement,
            reserved = validation.Reserved,
            currentRemaining = validation.Entitlement - validation.Reserved,
            requested = validation.Requested,
            remainingAfter = validation.RemainingAfter,
            unit = validation.Unit
        });
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

        var profileEligibility = await EmployeeRequestEligibility.CheckAsync(
            _dbContext, employeeId, HttpContext.RequestAborted);
        if (!profileEligibility.IsEligible)
            return await RequestProfileBlockedAsync(profileEligibility);

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

        // حارس التصويت المغلق: المعرّفان يأتيان من النموذج. يتحقّق أمرين معاً —
        // (١) الاستطلاع منشورٌ ومن شركة الموظف أو مشترك، فلا يُصوَّت على استطلاع
        // شركةٍ أخرى، و(٢) الخيار يخصّ هذا الاستطلاع بعينه، وإلا سُجّل صوتٌ لخيارٍ
        // من استطلاعٍ آخر فأفسد نتيجتيهما.
        var votable = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            """
SELECT COUNT(1)
FROM EmployeePolls p
INNER JOIN EmployeePollOptions o ON o.PollId = p.Id AND o.Id = @OptionId
WHERE p.Id = @PollId
  AND p.IsPublished = 1
  AND (p.CompanyId IS NULL
       OR p.CompanyId = (SELECT e.CompanyId FROM Employees e WHERE e.Id = @EmployeeId));
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@PollId", PollVote.PollId);
                HrmsDatabase.AddParameter(command, "@OptionId", PollVote.OptionId);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
            });

        if (votable == 0)
        {
            StatusMessage = "الاستطلاع غير متاح.";
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


    public async Task<IActionResult> OnPostCreateRequestAsync(string? returnTab,IFormFile? requestAttachment)
    {
        await EmployeeEngagementSchema.EnsureAsync(_dbContext);

        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            StatusMessage = "لا يمكن إرسال الطلب لأن المستخدم غير مرتبط بموظف.";
            return RedirectToPage(new { tab = returnTab ?? "requests" });
        }

        var profileEligibility = await EmployeeRequestEligibility.CheckAsync(
            _dbContext, employeeId, HttpContext.RequestAborted);
        if (!profileEligibility.IsEligible)
            return await RequestProfileBlockedAsync(profileEligibility);

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

        var actionCode = SelfServiceAccessPolicy.ActionForRequestType(type);
        if (actionCode is null || !await SelfServiceAccessPolicy.IsAllowedAsync(_dbContext, HttpContext, actionCode))
            return Forbid();

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

        // نفس الشرط الحازم على المسار المبسّط: لا يُقدَّم طلب بوقت على يوم ناقص البصمة.
        var reqTypeDef = (await RequestTypeStore.ListTypesAsync(_dbContext, onlyActive: true))
            .FirstOrDefault(x => x.Name == type);
        if (reqTypeDef is { NeedsTime: true })
        {
            var incompleteDay = await FindIncompletePunchDayAsync(
                employeeId,
                DateOnly.FromDateTime(fromDate.Value.Date),
                DateOnly.FromDateTime((toDate ?? fromDate).Value.Date));
            if (!string.IsNullOrEmpty(incompleteDay))
            {
                StatusMessage = IncompletePunchMessage(incompleteDay);
                return RedirectToPage(new { tab = returnTab ?? "requests" });
            }
        }

        string? attachmentPath=null;
        if(requestAttachment is { Length: >0 })
        {
            attachmentPath=await _protectedFiles.SaveAsync(requestAttachment,employeeId,"request",HttpContext.RequestAborted);
            if(attachmentPath is null)
            {
                StatusMessage="المرفق غير صالح؛ استخدم PDF أو صورة ضمن الحجم المسموح.";
                return RedirectToPage(new { tab = returnTab ?? "requests" });
            }
        }

        var requestId = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            """
INSERT INTO SelfServiceRequests
(EmployeeId, RequestType, CreatedAt, FromDate, ToDate, Reason, Status,AttachmentPath,RequestSource)
VALUES
(@EmployeeId, @RequestType, SYSUTCDATETIME(), @FromDate, @ToDate, @Reason, 'Pending',@AttachmentPath,N'SelfService');
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@RequestType", type);
                HrmsDatabase.AddParameter(command, "@FromDate", fromDate.Value);
                HrmsDatabase.AddParameter(command, "@ToDate", toDate);
                HrmsDatabase.AddParameter(command, "@Reason", reason);
                HrmsDatabase.AddParameter(command, "@AttachmentPath",(object?)attachmentPath??DBNull.Value);
            });

        // سريان الموافقات: حلّ القالب وتجميد خطوات اللجنة على الطلب.
        if (requestId > 0)
        {
            var start=await ApprovalWorkflowEngine.StartAsync(_dbContext, requestId, type, employeeId);
            if(!start.Ok)
            {
                StatusMessage=start.Message;
                return RedirectToPage(new { tab = returnTab ?? "requests" });
            }
        }

        StatusMessage = $"تم إرسال طلب {type} بنجاح وهو الآن قيد المراجعة.";
        return RedirectToPage(new { tab = returnTab ?? "requests" });
    }

    public async Task<IActionResult> OnPostResubmitReturnedAsync(
        int id, string revisedReason, DateTime? revisedFrom, DateTime? revisedTo)
    {
        var employeeId=await ResolveEmployeeIdAsync();
        if (employeeId<=0) return Forbid();
        var profileEligibility = await EmployeeRequestEligibility.CheckAsync(
            _dbContext, employeeId, HttpContext.RequestAborted);
        if (!profileEligibility.IsEligible)
            return await RequestProfileBlockedAsync(profileEligibility);
        var result=await ApprovalWorkflowEngine.ResubmitReturnedAsync(
            _dbContext,id,employeeId,revisedReason??string.Empty,revisedFrom,revisedTo);
        StatusMessage=result.Message;
        return RedirectToPage(new { tab="requests" });
    }

    public async Task<IActionResult> OnPostCancelRequestAsync(int id, string? cancelReason)
    {
        var employeeId=await ResolveEmployeeIdAsync();
        if(employeeId<=0) return Forbid();
        var result=await ApprovalWorkflowEngine.CancelByRequesterAsync(
            _dbContext,id,employeeId,User.Identity?.Name ?? employeeId.ToString(),cancelReason);
        StatusMessage=result.Message;
        return RedirectToPage(new { tab="requests" });
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
            StatusMessage = "تعذّر تحديد الموظف.";
            return RedirectToPage(new { tab = "requests" });
        }

        var profileEligibility = await EmployeeRequestEligibility.CheckAsync(
            _dbContext, employeeId, HttpContext.RequestAborted);
        if (!profileEligibility.IsEligible)
            return await RequestProfileBlockedAsync(profileEligibility);

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

        // ضوابط النوع من المتجر الداينمك. EffectCode هو الهوية الثابتة للتنفيذ والصلاحيات؛
        // اسم العرض قابل للتغيير من الإدارة ويُستخدم فقط كـ fallback للصفوف القديمة.
        var typeDef = (await RequestTypeStore.ListTypesAsync(_dbContext, onlyActive: true))
            .FirstOrDefault(x => x.Name == typeLabel);
        if (typeDef is null)
        {
            StatusMessage = "نوع الطلب غير متاح أو غير مفعّل.";
            return RedirectToPage(new { tab = "requests" });
        }

        var actionCode = SelfServiceAccessPolicy.ActionForEffectCode(RequestTypeEffectCatalog.EffectiveCode(typeDef))
            ?? SelfServiceAccessPolicy.ActionForRequestType(typeDef.Name);
        if (actionCode is null ||
            !await SelfServiceAccessPolicy.IsAllowedAsync(_dbContext, HttpContext, actionCode))
            return Forbid();

        if (typeDef is { NeedsTime: true } &&
            (string.IsNullOrWhiteSpace(fromTime) || string.IsNullOrWhiteSpace(toTime)))
        {
            StatusMessage = "يرجى تحديد وقت البداية والنهاية.";
            return RedirectToPage(new { tab = "requests" });
        }
        // متطلبات المرفق والسبب تُحسم حصراً داخل CompanyLeavePolicyStore أدناه.
        // هذا يحترم AttachmentRequiredOverride على مستوى الشركة ولا يخلق حارسين متعارضين.
        TimeSpan? startTs = TimeSpan.TryParse(fromTime, out var stTs) ? stTs : null;
        TimeSpan? endTs = TimeSpan.TryParse(toTime, out var etTs) ? etTs : null;

        // تقاطع منتصف الليل يُحسم قبل بوابة البصمات حتى يشمل الحارس الخادمي اليوم التالي.
        if (typeDef is { NeedsTime: true } && startTs.HasValue && endTs.HasValue && etTs <= stTs)
        {
            to = from.Value.AddDays(1);
        }

        // شرط حازم وجازم: يُمنع منعاً باتّاً تقديم أي طلب يحمل وقتاً إذا كان أحد أيامه
        // يحمل «بصمة ناقصة» (نسيان بصمة) — ويشمل Cross Midnight بعد توسيع المدى أعلاه.
        if (typeDef is { NeedsTime: true })
        {
            var incompleteDay = await FindIncompletePunchDayAsync(
                employeeId,
                DateOnly.FromDateTime(from.Value.Date),
                DateOnly.FromDateTime(to.Value.Date));
            if (!string.IsNullOrEmpty(incompleteDay))
            {
                StatusMessage = IncompletePunchMessage(incompleteDay);
                return RedirectToPage(new { tab = "requests" });
            }
        }

        var days = (decimal)((to.Value.Date - from.Value.Date).Days + 1);

        if (typeDef?.AllowedDays is int maxDays && days > maxDays)
        {
            StatusMessage = $"عدد الأيام يتجاوز المسموح ({maxDays} يوم) لهذا النوع.";
            return RedirectToPage(new { tab = "requests" });
        }

        // نفس محرك سياسة الشركة هو بوابة الحقيقة لكل أنواع الإجازات والمغادرات.
        // لا نستخدم LeaveBalanceCalculator القديم هنا لأنه لا يعرف الرصيد المشترك،
        // الساعات، التراكم، الحد السالب، مدة الاستحقاق أو المرفقات الخاصة بالشركة.
        var policyValidation = await CompanyLeavePolicyStore.ValidateRequestAsync(
            _dbContext,
            employeeId,
            currentRequestId: 0,
            requestTypeId: typeDef?.Id,
            requestTypeName: typeLabel,
            fromDate: DateOnly.FromDateTime(from.Value.Date),
            toDate: DateOnly.FromDateTime(to.Value.Date),
            startTime: startTs,
            endTime: endTs,
            reason: reason,
            hasAttachment: attachment is { Length: > 0 });
        if (!policyValidation.Ok)
        {
            StatusMessage = policyValidation.Message;
            return RedirectToPage(new { tab = "requests" });
        }

        // حفظ المرفق (صورة اختيارية).
        string? attachmentPath = null;
        if (attachment is { Length: > 0 })
        {
            // المرحلة 6: مرفق الطلب (تقرير طبي مثلاً) خارج wwwroot — كان يُخدَم
            // من مسار عام يخمّنه أي أحد. القراءة الآن عبر /files بفحص صلاحية.
            attachmentPath = await _protectedFiles.SaveAsync(
                attachment, employeeId, "request", HttpContext.RequestAborted);
        }

        reason = string.IsNullOrWhiteSpace(reason) ? "تم الإرسال من بوابة الموظف" : reason.Trim();

        var requestId = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            """
INSERT INTO SelfServiceRequests
(EmployeeId, RequestTypeId, RequestType, CreatedAt, FromDate, ToDate, StartTime, EndTime, Reason, Status, DaysCount, AttachmentPath, RequestSource)
VALUES
(@EmployeeId, @RequestTypeId, @RequestType, SYSUTCDATETIME(), @FromDate, @ToDate, @StartTime, @EndTime, @Reason, 'Pending', @DaysCount, @AttachmentPath, N'SelfService');
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@RequestTypeId", (object?)typeDef?.Id ?? DBNull.Value);
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
            var start=await ApprovalWorkflowEngine.StartAsync(_dbContext, requestId, typeLabel, employeeId);
            if(!start.Ok)
            {
                StatusMessage=start.Message;
                return RedirectToPage(new { tab = "requests" });
            }
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
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_dbContext, HttpContext, "PunchCorrection")) return Forbid();
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            StatusMessage = "لا يمكن إرسال الطلب لأن المستخدم غير مرتبط بموظف.";
            return RedirectToPage(new { tab = returnTab ?? "requests" });
        }

        var profileEligibility = await EmployeeRequestEligibility.CheckAsync(
            _dbContext, employeeId, HttpContext.RequestAborted);
        if (!profileEligibility.IsEligible)
            return await RequestProfileBlockedAsync(profileEligibility);

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

        var (ok, message) = await MissingPunchRequestStore.SaveAsync(
            _dbContext, CompanyScope.Unrestricted(), new MissingPunchRequestStore.Request
        {
            EmployeeId = employeeId,
            PunchAt = punchAt.Value,
            PunchType = derivedType,
            Reason = string.IsNullOrWhiteSpace(form["MpReason"]) ? null : form["MpReason"].ToString().Trim(),
            Source = "خدمة ذاتية"
        }, User.Identity?.Name ?? "employee", employeeId);

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
            return new JsonResult(new { punches = Array.Empty<object>() });

        var times = await PunchTypingEngine.DayPunchTimesAsync(_dbContext, employeeId, d);
        var typed = PunchTypingEngine.Derive(times);
        return new JsonResult(new
        {
            punches = typed.Select(p => new { at = p.At.ToString("HH:mm"), type = p.Type }).ToArray()
        });
    }

    /// <summary>نصّ المنع الموحّد ليوم يحمل بصمة ناقصة — يستخدمه الإرسال والبوابة الحيّة معاً.</summary>
    private static string IncompletePunchMessage(string day) =>
        $"تعذّر تقديم الطلب: يوم {day} يحمل بصمة ناقصة (نسيان بصمة) في سجل الحضور. يجب معالجة البصمة أولاً قبل تقديم أي طلب بوقت لذلك اليوم.";

    private sealed record PunchDaySummary(
        DateOnly Date,
        IReadOnlyList<PunchTypingEngine.TypedPunch> Punches)
    {
        public bool IsIncomplete => Punches.Count % 2 != 0;
    }

    /// <summary>
    /// يقرأ كل بصمات المدى ويصنّفها بنفس محرك الدخول/الخروج المستخدم في شاشة الحضور.
    /// هذا مهم خصوصاً لبصمة الموبايل: بصمة الخروج تُخزّن في صف يكون فيه CheckIn وCheckOut
    /// متساويين، لذلك لا يصح اعتبار امتلاء العمودين وحده دليلاً على اكتمال اليوم.
    /// </summary>
    private async Task<List<PunchDaySummary>> LoadPunchDaySummariesAsync(
        int employeeId, DateOnly from, DateOnly to)
    {
        if (to < from) (from, to) = (to, from);

        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT AttendanceDate, CheckIn, CheckOut
FROM AttendanceRecords
WHERE EmployeeId = @Emp
  AND AttendanceDate BETWEEN @From AND @To
  AND ISNULL(IsDeleted, 0) = 0
ORDER BY AttendanceDate, CheckIn, CheckOut;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Emp", employeeId);
                HrmsDatabase.AddParameter(command, "@From", from);
                HrmsDatabase.AddParameter(command, "@To", to);
            },
            reader => new
            {
                Date = HrmsDatabase.GetDateOnly(reader, "AttendanceDate"),
                CheckIn = HrmsDatabase.GetDateTime(reader, "CheckIn"),
                CheckOut = HrmsDatabase.GetDateTime(reader, "CheckOut")
            });

        var byDate = rows
            .Where(row => row.Date.HasValue)
            .GroupBy(row => row.Date!.Value)
            .ToDictionary(group => group.Key, group =>
            {
                var times = new List<DateTime>();
                foreach (var row in group)
                {
                    if (row.CheckIn.HasValue) times.Add(row.CheckIn.Value);
                    if (row.CheckOut.HasValue && row.CheckOut != row.CheckIn) times.Add(row.CheckOut.Value);
                }

                return (IReadOnlyList<PunchTypingEngine.TypedPunch>)PunchTypingEngine.Derive(times.Distinct());
            });

        var result = new List<PunchDaySummary>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            result.Add(new PunchDaySummary(
                date,
                byDate.TryGetValue(date, out var punches)
                    ? punches
                    : Array.Empty<PunchTypingEngine.TypedPunch>()));
        }
        return result;
    }

    /// <summary>
    /// يبحث عن أول يوم في المدى يحمل عدداً فردياً من البصمات بعد تصنيفها زمنياً؛ أي
    /// دخولاً بلا خروج مقابل. يُرجِع اليوم بصيغة yyyy-MM-dd أو null عند اكتمال المدى.
    /// </summary>
    private async Task<string?> FindIncompletePunchDayAsync(int employeeId, DateOnly from, DateOnly to)
    {
        var incomplete = (await LoadPunchDaySummariesAsync(employeeId, from, to))
            .FirstOrDefault(day => day.IsIncomplete);
        return incomplete?.Date.ToString("yyyy-MM-dd");
    }

    /// <summary>
    /// بوابة حيّة (AJAX) للطلبات الزمنية: بمجرد اختيار الموظف للتاريخ تُخبر الواجهة إن
    /// كان أحد أيام المدى يحمل نسيان بصمة، فيُمنع الإرسال ويظهر السبب فوراً قبل ملء
    /// بقية النموذج (بدل أن يُرفض بعد الإرسال).
    /// </summary>
    public async Task<IActionResult> OnGetTimeGateAsync(string? from, string? to)
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0 || !DateOnly.TryParse(from, out var f))
            return new JsonResult(new { blocked = false });
        if (!DateOnly.TryParse(to, out var t)) t = f;

        var summaries = await LoadPunchDaySummariesAsync(employeeId, f, t);
        var incomplete = summaries.FirstOrDefault(day => day.IsIncomplete);
        var day = incomplete?.Date.ToString("yyyy-MM-dd");
        var punches = new List<object>();
        foreach (var summary in summaries)
        {
            var ordered = summary.Punches.OrderBy(punch => punch.At).ToArray();
            var workday = await CompanyLeavePolicyStore.GetWorkdaySnapshotAsync(
                _dbContext, employeeId, summary.Date);
            var firstIn = ordered.FirstOrDefault(punch => punch.Type == "In");
            var lastOut = ordered.LastOrDefault(punch => punch.Type == "Out");
            decimal actualAttendanceMinutes = 0m;
            for (var index = 0; index + 1 < ordered.Length; index += 2)
            {
                var duration = ordered[index + 1].At - ordered[index].At;
                if (duration > TimeSpan.Zero)
                    actualAttendanceMinutes += (decimal)duration.TotalMinutes;
            }

            punches.Add(new
            {
                date = summary.Date.ToString("yyyy-MM-dd"),
                punchCount = ordered.Length,
                firstIn = firstIn?.At.ToString("HH:mm"),
                lastOut = lastOut?.At.ToString("HH:mm"),
                shiftName = workday.ShiftName,
                scheduledStart = workday.ScheduledStart,
                scheduledEnd = workday.ScheduledEnd,
                scheduledHours = workday.ScheduledHours,
                dayKind = workday.DayKind,
                isWorkingDay = workday.IsWorking,
                actualAttendanceMinutes = Math.Round(actualAttendanceMinutes, 0, MidpointRounding.AwayFromZero),
                missingPunch = summary.IsIncomplete,
                punches = ordered.Select((punch, index) => new
                {
                    index = index + 1,
                    at = punch.At.ToString("HH:mm"),
                    type = punch.Type
                }).ToArray(),
                checkIns = ordered
                    .Where(punch => punch.Type == "In")
                    .Select(punch => punch.At.ToString("HH:mm"))
                    .ToArray(),
                checkOuts = ordered
                    .Where(punch => punch.Type == "Out")
                    .Select(punch => punch.At.ToString("HH:mm"))
                    .ToArray()
            });
        }

        return new JsonResult(new
        {
            blocked = !string.IsNullOrEmpty(day),
            day,
            message = string.IsNullOrEmpty(day) ? null : IncompletePunchMessage(day),
            punches
        });
    }

    /// <summary>
    /// طلب «تعديل بياناتي»: يجمع الحقول التي غيّرها الموظف (new_&lt;key&gt;) ويقارنها
    /// بالقيم الحالية، فيُنشئ طلب خدمة ذاتية يمرّ عبر لجنة الموافقة. لا يُطبَّق على
    /// الملف إلا بعد الاعتماد النهائي (خلاف التعديل المباشر بشاشة «ملفي»).
    /// </summary>
    public async Task<IActionResult> OnPostSubmitDataChangeAsync(string? returnTab)
    {
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_dbContext, HttpContext, "UpdateMyData")) return Forbid();
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            StatusMessage = "لا يمكن إرسال الطلب لأن المستخدم غير مرتبط بموظف.";
            return RedirectToPage(new { tab = returnTab ?? "requests" });
        }

        var profileEligibility = await EmployeeRequestEligibility.CheckAsync(
            _dbContext, employeeId, HttpContext.RequestAborted);
        if (!profileEligibility.IsEligible)
            return await RequestProfileBlockedAsync(profileEligibility);

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
INSERT INTO SelfServiceRequests (EmployeeId, RequestType, CreatedAt, Reason, Status, RequestSource)
VALUES (@Emp, @Type, SYSUTCDATETIME(), @Reason, 'Pending', N'SelfService');
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

        var start=await ApprovalWorkflowEngine.StartAsync(_dbContext, requestId, DataChangeRequestStore.RequestTypeLabel, employeeId);
        if(!start.Ok)
        {
            StatusMessage=start.Message;
            return RedirectToPage(new { tab = returnTab ?? "requests" });
        }
        StatusMessage = $"تم إرسال طلب تعديل البيانات ({saved} حقل) وهو الآن قيد المراجعة.";
        return RedirectToPage(new { tab = returnTab ?? "requests" });
    }

    /// <summary>
    /// البصمة عبر الإنترنت (بصم ذاتي من البوابة — نمط كيان قسم 36.ج): يسجّل بصمة
    /// دخول/خروج بوقت الخادم الحالي بمصدر «موبايل»، فتدخل اشتقاق اليومية كأي بصمة.
    /// </summary>
    public async Task<IActionResult> OnPostOnlinePunchAsync(
        string? punchType, string? returnTab, double? geoLat, double? geoLng, string? bioToken)
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            StatusMessage = "لا يمكن تسجيل البصمة لأن المستخدم غير مرتبط بموظف.";
            return RedirectToPage(new { tab = returnTab ?? "attendance" });
        }

        var now = DateTime.Now;

        // زر واحد بلا اختيار نوع: النوع يُستنتج تلقائياً بالأسبقية الزمنية بين بصمات
        // اليوم (كل المصادر) — أول بصمة دخول، التالية خروج، وهكذا (نفس محرك نسيان
        // البصمة). تمرير النوع صراحةً يبقى مدعوماً للتوافق.
        string type;
        if (punchType is "In" or "Out")
        {
            type = punchType;
        }
        else
        {
            var todayTimes = await PunchTypingEngine.DayPunchTimesAsync(
                _dbContext, employeeId, DateOnly.FromDateTime(now));
            type = PunchTypingEngine.DeriveTypeFor(todayTimes, now);
        }
        // الإثبات البيولوجي (إن وُجد) يُستهلك مرة واحدة ويُتحقق أنه لهذا الموظف وضمن نافذته.
        var biometricVerified = WebAuthnProofStore.Consume(bioToken, employeeId);
        var result = await OnlinePunchStore.RecordAsync(
            _dbContext, employeeId, type, now, null, geoLat, geoLng, biometricVerified);

        // نمرّر نتيجة البصمة عبر معطيات الرابط (أعداد صحيحة آمنة ثقافياً) فتُعاد صياغة الرسالة
        // في OnGet — بديل موثوق لا يعتمد على بقاء TempData عبر إعادة التوجيه (PRG).
        var tab = returnTab ?? "attendance";
        return result.Status switch
        {
            OnlinePunchStore.PunchStatus.Recorded =>
                RedirectToPage(new { tab, punch = type == "Out" ? "out" : "in" }),
            OnlinePunchStore.PunchStatus.TooSoonForCheckout =>
                RedirectToPage(new
                {
                    tab,
                    punch = "toosoon",
                    pminm = (int)Math.Round(result.MinCheckoutHours * 60),
                    prem = (int)Math.Ceiling(result.HoursRemaining * 60)
                }),
            OnlinePunchStore.PunchStatus.OutsideGeofence =>
                RedirectToPage(new { tab, punch = "geo" }),
            OnlinePunchStore.PunchStatus.BiometricRequired =>
                RedirectToPage(new { tab, punch = "bio" }),
            _ =>
                RedirectToPage(new { tab, punch = "dup" })
        };
    }

    /// <summary>
    /// يقصر أنواع الطلبات على ما ينطبق على هذا الموظف بمحرّك الشروط العام.
    ///
    /// نوعٌ **بلا شروط متاح للجميع** (<c>matchWhenEmpty: true</c>) — وهو ما يجعل
    /// إضافة هذه الطبقة **صفرَ تغييرٍ بالسلوك** حتى يضع HR شرطاً فعلياً. ولو كان
    /// الافتراض عكسه لاختفت كل أنواع الطلبات عن كل الموظفين لحظةَ النشر.
    ///
    /// وحقلا <c>Gender</c> و<c>ServiceMonths</c> القديمان **لا يُنفَّذان هنا**: لم
    /// يكونا يُنفَّذان أصلاً، وتفعيلهما اليوم يخفي عن الموظفين أنواعاً يرونها.
    /// </summary>
    private async Task<List<RequestTypeStore.ReqType>> FilterEligibleTypesAsync(
        List<RequestTypeStore.ReqType> types, int employeeId)
    {
        if (types.Count == 0 || types.All(t => string.IsNullOrWhiteSpace(t.ConditionsJson)))
        {
            return types;
        }

        var rows = await HrConditionFacts.LoadAsync(_dbContext, employeeId);
        if (rows.Count == 0)
        {
            return types;
        }

        var facts = HrConditionFacts.Build(rows[0], DateOnly.FromDateTime(DateTime.Today));

        return types
            .Where(type => HrConditions.Matches(
                HrConditions.Deserialize(type.ConditionsJson), facts, matchWhenEmpty: true))
            .ToList();
    }

    private async Task LoadAsync()
    {
        await EmployeeEngagementSchema.EnsureAsync(_dbContext);

        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            IsDemoMode = true;
        }

        if (employeeId <= 0)
        {
            var localizedDemoName =
                await LocalizeUiTextAsync(
                    "موظف تجريبي");

            Employee = EmployeePortalEmployee.Empty with
            {
                FullName = localizedDemoName,
                Position = "Employee"
            };
            IsDemoMode = true;
            return;
        }

        Employee = await LoadEmployeeAsync(employeeId)
                   ?? EmployeePortalEmployee.Empty;

        var localizedEmployee =
            await EmployeeBusinessDataDisplayLocalizer
                .GetEmployeeBusinessDataAsync(
                    _dbContext,
                    new[] { employeeId },
                    HttpContext.RequestAborted);

        if (localizedEmployee.TryGetValue(
                employeeId,
                out var localizedDisplay))
        {
            Employee = Employee with
            {
                FullName = localizedDisplay.FullName,
                Position = localizedDisplay.Position
                           ?? Employee.Position,
                DepartmentName =
                    localizedDisplay.DepartmentName,
                BranchName =
                    localizedDisplay.BranchName
            };
        }

        // Tabs are switched client-side without a round-trip, so the full profile
        // must be present even when the initial tab is Home.
        FullProfile = await LoadFullProfileAsync(employeeId);

        Compensation = await LoadCompensationAsync(employeeId);
        Announcements = await LoadAnnouncementsAsync(Employee);
        Polls = await LoadPollsAsync(Employee);
        Requests = await LoadRequestsAsync(employeeId, RequestId);
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
        DisciplinaryHistory = await LoadDisciplinaryHistoryAsync(employeeId);
        PendingAssetAcknowledgments = await LoadPendingAssetAcknowledgmentsAsync(employeeId);
        MyMissingPunches = await MissingPunchRequestStore.ListAsync(_dbContext, SmartAttendance.Web.Infrastructure.Security.CompanyScope.Unrestricted(),
            new MissingPunchRequestStore.Filter { EmployeeId = employeeId });
        // مسار ذاتي: المعرّف من جلسة الموظف لا من المتصفح، فالنطاق غير مقيَّد عمداً.
        MyOnlinePunches = await OnlinePunchStore.ListAsync(
            _dbContext, SmartAttendance.Web.Infrastructure.Security.CompanyScope.Unrestricted(),
            new OnlinePunchStore.Filter { EmployeeId = employeeId, Top = 200 });

        // إنفاذ التأكيد البيولوجي: لا نستعلم عن المفاتيح إلا والراية مفعّلة (الافتراضي لا).
        try
        {
            RequireBiometricPunch = await OnlinePunchStore.GetRequireBiometricAsync(_dbContext)
                && await WebAuthnCredentialStore.HasActiveForEmployeeAsync(_dbContext, employeeId);
        }
        catch { RequireBiometricPunch = false; }

        // أنواع الطلبات الداينمك (تبويبات + أنواع بضوابطها) لشاشة الإجازة.
        try
        {
            await RequestTypeStore.EnsureAsync(_dbContext);
            ReqCategories = await RequestTypeStore.ListCategoriesAsync(_dbContext, onlyActive: true);
            ReqTypes = await RequestTypeStore.ListTypesAsync(_dbContext, onlyActive: true);
            ReqTypes = await FilterEligibleTypesAsync(ReqTypes, employeeId);

            LeaveBalances = await CompanyLeavePolicyStore.GetBalanceSnapshotsAsync(
                _dbContext, employeeId, DateOnly.FromDateTime(DateTime.Today));

            var companyId = await HrmsDatabase.ScalarAsync<int>(
                _dbContext,
                "SELECT ISNULL(CompanyId,0) FROM Employees WHERE Id=@EmployeeId AND ISNULL(IsDeleted,0)=0;",
                command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId));
            LeavePolicies = companyId > 0
                ? await CompanyLeavePolicyStore.ListForCompanyAsync(_dbContext, companyId, onlyActive: true)
                : new();
        }
        catch
        {
            ReqCategories = new();
            ReqTypes = new();
            LeaveBalances = new();
            LeavePolicies = new();
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
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_dbContext, HttpContext, "UpdateMyData")) return Forbid();
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0) return Forbid();
        var ok = await DataChangeRequestStore.DeletePendingRequestAsync(_dbContext, id, employeeId);
        StatusMessage = ok ? "تم حذف طلب التعديل المعلّق." : "تعذّر الحذف (الطلب غير موجود أو تمّ البتّ فيه).";
        return RedirectToPage(new { tab = "requests" });
    }

    public List<CompanyLeavePolicyStore.BalanceSnapshot> LeaveBalances { get; set; } = new();
    public List<CompanyLeavePolicyStore.Policy> LeavePolicies { get; set; } = new();
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
            return Forbid();
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

    /// <summary>سجل انضباط الموظف — بطاقة مخالفة بإجرائها وحالتها وأثرها المالي (لتبويب الانضباط).</summary>
    public sealed class DisciplinaryRecord
    {
        public string ReferenceNo { get; set; } = string.Empty;
        public DateOnly? EventDate { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ActionStatus { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string FinalPenaltyAction { get; set; } = string.Empty;
        public decimal DeductionAmount { get; set; }
        public string ReplyStatus { get; set; } = string.Empty;
        public string EmployeeReply { get; set; } = string.Empty;
    }

    /// <summary>كامل مخالفات الموظف (سجل الانضباط) — يُعرض بتبويب «التقييم والانضباط».</summary>
    public List<DisciplinaryRecord> DisciplinaryHistory { get; set; } = new();

    private async Task<List<DisciplinaryRecord>> LoadDisciplinaryHistoryAsync(int employeeId)
    {
        await ViolationCaseSchema.EnsureAsync(_dbContext);
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT ReferenceNo,
       EventDate,
       ISNULL(ViolationCategory, N'') AS ViolationCategory,
       ISNULL(ViolationTitle, N'') AS ViolationTitle,
       ISNULL(ActionStatus, N'') AS ActionStatus,
       ISNULL(Status, N'') AS Status,
       ISNULL(FinalPenaltyAction, N'') AS FinalPenaltyAction,
       ISNULL(DeductionAmount, 0) AS DeductionAmount,
       ISNULL(EmployeeReplyStatus, N'NotRequested') AS EmployeeReplyStatus,
       ISNULL(EmployeeReply, N'') AS EmployeeReply
FROM EmployeeViolationCases
WHERE EmployeeId = @EmployeeId AND ISNULL(IsDeleted, 0) = 0
ORDER BY EventDate DESC, Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new DisciplinaryRecord
            {
                ReferenceNo = HrmsDatabase.GetString(reader, "ReferenceNo"),
                EventDate = HrmsDatabase.GetDateOnly(reader, "EventDate"),
                Category = HrmsDatabase.GetString(reader, "ViolationCategory"),
                Title = HrmsDatabase.GetString(reader, "ViolationTitle"),
                ActionStatus = HrmsDatabase.GetString(reader, "ActionStatus"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                FinalPenaltyAction = HrmsDatabase.GetString(reader, "FinalPenaltyAction"),
                DeductionAmount = GetDecimal(reader, "DeductionAmount"),
                ReplyStatus = HrmsDatabase.GetString(reader, "EmployeeReplyStatus"),
                EmployeeReply = HrmsDatabase.GetString(reader, "EmployeeReply")
            });
    }

    /// <summary>ردّ الموظف على مخالفة (حق الدفاع) — يُحفظ نصاً ويُشعر HR.</summary>
    public async Task<IActionResult> OnPostViolationReplyAsync(int id, string reply, string? returnTab)
    {
        var employeeId = await ResolveEmployeeIdAsync();
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

    private async Task<string> LocalizeUiTextAsync(
        string source)
    {
        var catalog =
            await _localizationDictionary.GetCatalogAsync(
                CultureInfo.CurrentUICulture.Name,
                HttpContext.RequestAborted);

        return catalog.TryGetValue(
                   source,
                   out var translated) &&
               !string.IsNullOrWhiteSpace(translated)
            ? translated
            : source;
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

    private async Task<IActionResult> RequestProfileBlockedAsync(
        EmployeeRequestEligibility.Result eligibility)
    {
        // رسالة هذه المحاولة لا تدخل TempData ولا تعيش بعد الصفحة الحالية.
        StatusMessage = null;
        InlineRequestError = eligibility.Message;
        Tab = "requests";
        await LoadAsync();
        return Page();
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

    private async Task<EmployeePortalFullProfile> LoadFullProfileAsync(int employeeId)
    {
        var profile = new EmployeePortalFullProfile();

        var basics = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1
    ISNULL(FirstNameEn,'') AS FirstNameEn,
    ISNULL(SecondNameEn,'') AS SecondNameEn,
    ISNULL(ThirdNameEn,'') AS ThirdNameEn,
    ISNULL(LastNameEn,'') AS LastNameEn,
    ISNULL(PassportNo,'') AS PassportNo,
    ISNULL(Gender,'') AS Gender,
    ISNULL(MaritalStatus,'') AS MaritalStatus,
    ISNULL(Country,'') AS Country,
    ISNULL(Nationality,'') AS Nationality,
    ISNULL(Religion,'') AS Religion,
    ISNULL(MotherCountry,'') AS MotherCountry,
    ISNULL(MotherCity,'') AS MotherCity,
    ISNULL(PersonalEmail,'') AS PersonalEmail,
    ISNULL(PhoneExtension,'') AS PhoneExtension,
    JoiningDate,
    ISNULL(WorkType,'') AS WorkType,
    ISNULL(JobGrade,'') AS JobGrade,
    ISNULL(ContractType,'') AS ContractType,
    ContractEndDate,
    ISNULL(EmploymentStatus,'') AS EmploymentStatus
FROM Employees
WHERE Id=@EmployeeId AND ISNULL(IsDeleted,0)=0;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new EmployeePortalFullProfile
            {
                FirstNameEn = HrmsDatabase.GetString(reader, "FirstNameEn"),
                SecondNameEn = HrmsDatabase.GetString(reader, "SecondNameEn"),
                ThirdNameEn = HrmsDatabase.GetString(reader, "ThirdNameEn"),
                LastNameEn = HrmsDatabase.GetString(reader, "LastNameEn"),
                PassportNo = HrmsDatabase.GetString(reader, "PassportNo"),
                Gender = HrmsDatabase.GetString(reader, "Gender"),
                MaritalStatus = HrmsDatabase.GetString(reader, "MaritalStatus"),
                Country = HrmsDatabase.GetString(reader, "Country"),
                Nationality = HrmsDatabase.GetString(reader, "Nationality"),
                Religion = HrmsDatabase.GetString(reader, "Religion"),
                MotherCountry = HrmsDatabase.GetString(reader, "MotherCountry"),
                MotherCity = HrmsDatabase.GetString(reader, "MotherCity"),
                PersonalEmail = HrmsDatabase.GetString(reader, "PersonalEmail"),
                PhoneExtension = HrmsDatabase.GetString(reader, "PhoneExtension"),
                JoiningDate = HrmsDatabase.GetDateTime(reader, "JoiningDate"),
                WorkType = HrmsDatabase.GetString(reader, "WorkType"),
                JobGrade = HrmsDatabase.GetString(reader, "JobGrade"),
                ContractType = HrmsDatabase.GetString(reader, "ContractType"),
                ContractEndDate = HrmsDatabase.GetDateTime(reader, "ContractEndDate"),
                EmploymentStatus = HrmsDatabase.GetString(reader, "EmploymentStatus")
            });

        profile = basics.FirstOrDefault() ?? profile;

        profile.IdentityDocuments = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT DocumentType, ISNULL(DocumentNumber,'') AS DocumentNumber,
       ISNULL(NationalNumber,'') AS NationalNumber,
       ISNULL(FamilyNumber,'') AS FamilyNumber,
       IssueDate, ExpiryDate,
       ISNULL(IssuingAuthority,'') AS IssuingAuthority,
       ISNULL(PlaceOfIssue,'') AS PlaceOfIssue,
       ISNULL(VerificationStatus,'') AS VerificationStatus
FROM EmployeeIdentityDocuments
WHERE EmployeeId=@EmployeeId AND IsCurrent=1
ORDER BY Id;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new EmployeePortalIdentityDocument
            {
                DocumentType = HrmsDatabase.GetString(reader, "DocumentType"),
                DocumentNumber = HrmsDatabase.GetString(reader, "DocumentNumber"),
                NationalNumber = HrmsDatabase.GetString(reader, "NationalNumber"),
                FamilyNumber = HrmsDatabase.GetString(reader, "FamilyNumber"),
                IssueDate = HrmsDatabase.GetDateTime(reader, "IssueDate"),
                ExpiryDate = HrmsDatabase.GetDateTime(reader, "ExpiryDate"),
                IssuingAuthority = HrmsDatabase.GetString(reader, "IssuingAuthority"),
                PlaceOfIssue = HrmsDatabase.GetString(reader, "PlaceOfIssue"),
                VerificationStatus = HrmsDatabase.GetString(reader, "VerificationStatus")
            });

        profile.Documents = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT Id,DocumentType,FileName,StoredPath,ExpiryDate,
       ISNULL(Notes,'') AS Notes,UploadedAt
FROM EmployeeDocuments
WHERE EmployeeId=@EmployeeId
ORDER BY UploadedAt DESC,Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new EmployeePortalDocument
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                DocumentType = HrmsDatabase.GetString(reader, "DocumentType"),
                FileName = HrmsDatabase.GetString(reader, "FileName"),
                StoredPath = HrmsDatabase.GetString(reader, "StoredPath"),
                ExpiryDate = HrmsDatabase.GetDateTime(reader, "ExpiryDate"),
                Notes = HrmsDatabase.GetString(reader, "Notes"),
                UploadedAt = HrmsDatabase.GetDateTime(reader, "UploadedAt")
            });

        var financialRows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1
    ISNULL(Currency,'') AS Currency,
    ISNULL(SalaryScale,'') AS SalaryScale,
    ISNULL(BasicSalary,0) AS BasicSalary,
    ISNULL(DailySalary,0) AS DailySalary,
    ISNULL(HourlyRate,0) AS HourlyRate,
    ISNULL(SocialSecurityType,'') AS SocialSecurityType,
    ISNULL(SocialSecurityNo,'') AS SocialSecurityNo,
    SocialSecurityJoinDate,
    ISNULL(TaxFile,'') AS TaxFile,
    ISNULL(TaxNo,'') AS TaxNo,
    ISNULL(PaymentMethod,'') AS PaymentMethod,
    ISNULL(BankName,'') AS BankName,
    ISNULL(BankBranch,'') AS BankBranch,
    ISNULL(UnitNo,'') AS UnitNo,
    ISNULL(Iban,'') AS Iban,
    ISNULL(CardNo,'') AS CardNo
FROM EmployeeFinancialInfos
WHERE EmployeeId=@EmployeeId AND ISNULL(IsDeleted,0)=0
ORDER BY ISNULL(UpdatedAt,CreatedAt) DESC, Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new EmployeePortalFinancialProfile
            {
                Currency = HrmsDatabase.GetString(reader, "Currency"),
                SalaryScale = HrmsDatabase.GetString(reader, "SalaryScale"),
                BasicSalary = GetDecimal(reader, "BasicSalary"),
                DailySalary = GetDecimal(reader, "DailySalary"),
                HourlyRate = GetDecimal(reader, "HourlyRate"),
                SocialSecurityType = HrmsDatabase.GetString(reader, "SocialSecurityType"),
                SocialSecurityNo = HrmsDatabase.GetString(reader, "SocialSecurityNo"),
                SocialSecurityJoinDate = HrmsDatabase.GetDateTime(reader, "SocialSecurityJoinDate"),
                TaxFile = HrmsDatabase.GetString(reader, "TaxFile"),
                TaxNo = HrmsDatabase.GetString(reader, "TaxNo"),
                PaymentMethod = HrmsDatabase.GetString(reader, "PaymentMethod"),
                BankName = HrmsDatabase.GetString(reader, "BankName"),
                BankBranch = HrmsDatabase.GetString(reader, "BankBranch"),
                UnitNo = HrmsDatabase.GetString(reader, "UnitNo"),
                Iban = HrmsDatabase.GetString(reader, "Iban"),
                CardNo = HrmsDatabase.GetString(reader, "CardNo")
            });
        profile.Financial = financialRows.FirstOrDefault() ?? new();

        var contractRows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1 ISNULL(ContractNo,'') AS ContractNo,
       ISNULL(ContractType,'') AS ContractType,
       FromDate,ToDate,IsCurrent,ISNULL(Note,'') AS Note
FROM EmployeeContracts
WHERE EmployeeId=@EmployeeId AND ISNULL(IsDeleted,0)=0
ORDER BY IsCurrent DESC, FromDate DESC, Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new EmployeePortalContractProfile
            {
                ContractNo = HrmsDatabase.GetString(reader, "ContractNo"),
                ContractType = HrmsDatabase.GetString(reader, "ContractType"),
                FromDate = HrmsDatabase.GetDateTime(reader, "FromDate"),
                ToDate = HrmsDatabase.GetDateTime(reader, "ToDate"),
                IsCurrent = HrmsDatabase.GetBool(reader, "IsCurrent"),
                Note = HrmsDatabase.GetString(reader, "Note")
            });
        profile.Contract = contractRows.FirstOrDefault() ?? new();

        profile.Dependents = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT Relation,Name,ISNULL(NameOther,'') AS NameOther,BirthDate,
       ISNULL(Gender,'') AS Gender,ISNULL(Nationality,'') AS Nationality,
       IsEmergencyContact,IsDependent,ISNULL(MobilePhone,'') AS MobilePhone,
       ISNULL(Note,'') AS Note
FROM EmployeeDependents
WHERE EmployeeId=@EmployeeId AND ISNULL(IsDeleted,0)=0
ORDER BY Relation,Id;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new EmployeePortalDependent
            {
                Relation = HrmsDatabase.GetInt(reader, "Relation"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                NameOther = HrmsDatabase.GetString(reader, "NameOther"),
                BirthDate = HrmsDatabase.GetDateTime(reader, "BirthDate"),
                Gender = HrmsDatabase.GetString(reader, "Gender"),
                Nationality = HrmsDatabase.GetString(reader, "Nationality"),
                IsEmergencyContact = HrmsDatabase.GetBool(reader, "IsEmergencyContact"),
                IsDependent = HrmsDatabase.GetBool(reader, "IsDependent"),
                MobilePhone = HrmsDatabase.GetString(reader, "MobilePhone"),
                Note = HrmsDatabase.GetString(reader, "Note")
            });

        profile.Records = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT RecordType,Title,ISNULL(Subtitle,'') AS Subtitle,
       ISNULL(Country,'') AS Country,ISNULL(RefNo,'') AS RefNo,
       FromDate,ToDate,Amount,IsCurrent,
       ISNULL(Gpa,'') AS Gpa,
       ISNULL(RefContactName,'') AS RefContactName,
       ISNULL(RefContactPosition,'') AS RefContactPosition,
       ISNULL(RefContactPhone,'') AS RefContactPhone,
       ISNULL(Note,'') AS Note
FROM EmployeeFileRecords
WHERE EmployeeId=@EmployeeId AND ISNULL(IsDeleted,0)=0
ORDER BY RecordType,FromDate DESC,Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new EmployeePortalProfileRecord
            {
                RecordType = HrmsDatabase.GetInt(reader, "RecordType"),
                Title = HrmsDatabase.GetString(reader, "Title"),
                Subtitle = HrmsDatabase.GetString(reader, "Subtitle"),
                Country = HrmsDatabase.GetString(reader, "Country"),
                RefNo = HrmsDatabase.GetString(reader, "RefNo"),
                FromDate = HrmsDatabase.GetDateTime(reader, "FromDate"),
                ToDate = HrmsDatabase.GetDateTime(reader, "ToDate"),
                Amount = GetDecimal(reader, "Amount"),
                IsCurrent = HrmsDatabase.GetBool(reader, "IsCurrent"),
                Gpa = HrmsDatabase.GetString(reader, "Gpa"),
                RefContactName = HrmsDatabase.GetString(reader, "RefContactName"),
                RefContactPosition = HrmsDatabase.GetString(reader, "RefContactPosition"),
                RefContactPhone = HrmsDatabase.GetString(reader, "RefContactPhone"),
                Note = HrmsDatabase.GetString(reader, "Note")
            });

        return profile;
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
  -- عزل الشركة (8D–8M): استطلاع شركةٍ أخرى موجَّه لـ«الكل» كان يظهر لموظفي كل
  -- الشركات ويقبل أصواتهم فيلوّث نتائجه. NULL = مشترك (السلوك القديم)، وموظفٌ
  -- بلا شركة يرى المشترك وحده — مغلق الفشل.
  AND (p.CompanyId IS NULL
       OR p.CompanyId = (SELECT e.CompanyId FROM Employees e WHERE e.Id = @EmployeeId))
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

    private async Task<List<EmployeePortalRequest>> LoadRequestsAsync(int employeeId, int? requestId = null)
    {
        var requests = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 15
    r.Id,
    r.RequestTypeId,
    r.RequestType,
    r.CreatedAt,
    r.FromDate,
    r.ToDate,
    r.StartTime,
    r.EndTime,
    punches.ActualCheckIn,
    punches.ActualCheckOut,
    ISNULL(r.Reason, '') AS Reason,
    r.Status,
    ISNULL(r.CurrentStep, '') AS CurrentStep,
    ISNULL(r.ReviewNote, '') AS ReviewNote
FROM SelfServiceRequests r
OUTER APPLY
(
    SELECT
        MIN(CASE
                WHEN ar.CheckOut IS NULL OR ar.CheckOut <> ar.CheckIn
                    THEN ar.CheckIn
            END) AS ActualCheckIn,
        MAX(ar.CheckOut) AS ActualCheckOut
    FROM AttendanceRecords ar
    WHERE ar.EmployeeId = r.EmployeeId
      AND ISNULL(ar.IsDeleted, 0) = 0
      AND ar.AttendanceDate >= CAST(COALESCE(r.FromDate, r.RequestDate, CAST(r.CreatedAt AS date)) AS date)
      AND ar.AttendanceDate <= CAST(COALESCE(r.ToDate, r.FromDate, r.RequestDate, CAST(r.CreatedAt AS date)) AS date)
) punches
WHERE r.EmployeeId = @EmployeeId
ORDER BY
    CASE WHEN @RequestId IS NOT NULL AND r.Id = @RequestId THEN 0 ELSE 1 END,
    r.CreatedAt DESC,
    r.Id DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@RequestId", requestId is > 0 ? requestId.Value : DBNull.Value);
            },
            reader => new EmployeePortalRequest
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                RequestTypeId = HrmsDatabase.GetNullableInt(reader, "RequestTypeId"),
                RequestType = HrmsDatabase.GetString(reader, "RequestType"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt"),
                FromDate = HrmsDatabase.GetDateTime(reader, "FromDate"),
                ToDate = HrmsDatabase.GetDateTime(reader, "ToDate"),
                StartTime = HrmsDatabase.GetTimeSpan(reader, "StartTime"),
                EndTime = HrmsDatabase.GetTimeSpan(reader, "EndTime"),
                ActualCheckIn = HrmsDatabase.GetDateTime(reader, "ActualCheckIn"),
                ActualCheckOut = HrmsDatabase.GetDateTime(reader, "ActualCheckOut"),
                Reason = HrmsDatabase.GetString(reader, "Reason"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                CurrentStep = HrmsDatabase.GetString(reader, "CurrentStep"),
                ReviewNote = HrmsDatabase.GetString(reader, "ReviewNote")
            });

        var flows = await ApprovalWorkflowEngine.GetFlowsAsync(_dbContext, requests.Select(request => request.Id));
        foreach (var request in requests)
            if (flows.TryGetValue(request.Id, out var flow))
                request.ApprovalFlow = flow;

        await PopulateOvertimeIntelligenceAsync(employeeId, requests);
        return requests;
    }

    private async Task PopulateOvertimeIntelligenceAsync(
        int employeeId, List<EmployeePortalRequest> requests)
    {
        if (requests.Count == 0) return;

        await RequestTypeStore.EnsureAsync(_dbContext);
        var types = await RequestTypeStore.ListTypesAsync(_dbContext, onlyActive: false);
        var byId = types.ToDictionary(type => type.Id);
        var byName = types
            .GroupBy(type => type.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var request in requests)
        {
            RequestTypeStore.ReqType? type = null;
            if (request.RequestTypeId is > 0 && byId.TryGetValue(request.RequestTypeId.Value, out var byIdentity))
                type = byIdentity;
            else if (!string.IsNullOrWhiteSpace(request.RequestType))
                byName.TryGetValue(request.RequestType, out type);

            request.IsOvertime = type is not null
                ? BulkRequestStore.ResolveEffect(type).Kind == BulkRequestStore.EffectKind.Overtime
                : ApprovalWorkflowEngine.ResolveRequestTypeKey(request.RequestType) == "Overtime";
        }

        var overtime = requests.Where(request => request.IsOvertime).ToList();
        if (overtime.Count == 0) return;

        var allowCrossMidnight = await AttendanceRequestPolicy.GetCrossMidnightAsync(_dbContext);
        var windows = new Dictionary<int, (DateTime Start, DateTime End)>();
        foreach (var request in overtime)
        {
            if (!request.FromDate.HasValue || !request.StartTime.HasValue || !request.EndTime.HasValue)
                continue;

            var duration = AttendanceRequestPolicy.Duration(
                request.StartTime.Value, request.EndTime.Value, allowCrossMidnight);
            if (duration <= TimeSpan.Zero) continue;

            var start = request.FromDate.Value.Date + request.StartTime.Value;
            var end = start + duration;
            windows[request.Id] = (start, end);
            request.RequestedOvertimeHours = Math.Round((decimal)duration.TotalHours, 2);
            request.ApprovedOvertimeHours = request.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase)
                ? request.RequestedOvertimeHours
                : 0m;
            request.PayrollEligible = request.ApprovedOvertimeHours > 0m;
        }

        if (windows.Count > 0)
        {
            var minDate = DateOnly.FromDateTime(windows.Values.Min(window => window.Start));
            var maxDate = DateOnly.FromDateTime(windows.Values.Max(window => window.End));
            var attendance = await LoadPunchDaySummariesAsync(employeeId, minDate, maxDate);

            foreach (var request in overtime)
            {
                if (!windows.TryGetValue(request.Id, out var window)) continue;
                decimal actualMinutes = 0m;
                foreach (var day in attendance)
                {
                    var punches = day.Punches.OrderBy(punch => punch.At).ToArray();
                    for (var index = 0; index + 1 < punches.Length; index += 2)
                    {
                        var pairStart = punches[index].At;
                        var pairEnd = punches[index + 1].At;
                        if (pairEnd <= pairStart) continue;
                        var overlapStart = pairStart > window.Start ? pairStart : window.Start;
                        var overlapEnd = pairEnd < window.End ? pairEnd : window.End;
                        if (overlapEnd > overlapStart)
                            actualMinutes += (decimal)(overlapEnd - overlapStart).TotalMinutes;
                    }
                }
                request.ActualAttendanceOvertimeHours =
                    Math.Round(actualMinutes / 60m, 2, MidpointRounding.AwayFromZero);
            }
        }

        await PayrollTransactionStore.EnsureAsync(_dbContext);
        var posted = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT Source, Hours, RateFactor, Status, IsLocked
FROM PayrollTransactions
WHERE EmployeeId=@EmployeeId
  AND TxType=N'Overtime'
  AND Source LIKE N'Approval:%';
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new
            {
                Source = HrmsDatabase.GetString(reader, "Source"),
                Hours = reader["Hours"] is decimal hours ? (decimal?)hours : null,
                RateFactor = reader["RateFactor"] is decimal factor ? (decimal?)factor : null,
                Status = HrmsDatabase.GetString(reader, "Status"),
                IsLocked = HrmsDatabase.GetBool(reader, "IsLocked")
            });

        var postedByRequest = posted
            .Select(row => new
            {
                Row = row,
                RequestId = row.Source.StartsWith("Approval:", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(row.Source["Approval:".Length..], out var id) ? id : 0
            })
            .Where(item => item.RequestId > 0)
            .GroupBy(item => item.RequestId)
            .ToDictionary(group => group.Key, group => group.First().Row);

        foreach (var request in overtime)
        {
            if (!postedByRequest.TryGetValue(request.Id, out var tx)) continue;
            request.PayrollPosted = true;
            request.PayrollStatus = tx.IsLocked ? "Locked" : tx.Status;
            request.PayrollRateFactor = tx.RateFactor ?? PayrollTransactionStore.DefaultRateFactor;
        }
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
    public string DisplayClock(TimeSpan? time) =>
        time.HasValue ? $"{(int)time.Value.TotalHours:00}:{time.Value.Minutes:00}" : "-";
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
        if (status.Equals("Returned", StringComparison.OrdinalIgnoreCase)) return "معاد للتعديل";
        if (status.Equals("Draft", StringComparison.OrdinalIgnoreCase)) return "مسودة تحتاج استكمالاً";
        return string.IsNullOrWhiteSpace(status) ? "-" : status;
    }

    public string ApprovalStepText(string status)
    {
        if (status.Equals("Current", StringComparison.OrdinalIgnoreCase)) return "المرحلة الحالية";
        if (status.Equals("Pending", StringComparison.OrdinalIgnoreCase)) return "بانتظار المرحلة";
        if (status.Equals("Approved", StringComparison.OrdinalIgnoreCase)) return "تمت الموافقة";
        if (status.Equals("Rejected", StringComparison.OrdinalIgnoreCase)) return "مرفوض";
        if (status.Equals("Returned", StringComparison.OrdinalIgnoreCase)) return "معاد للتعديل";
        if (status.Equals("WaitingRevision", StringComparison.OrdinalIgnoreCase)) return "بانتظار تعديل الموظف";
        if (status.Equals("Skipped", StringComparison.OrdinalIgnoreCase)) return "تم تجاوزها";
        return string.IsNullOrWhiteSpace(status) ? "-" : status;
    }

    public string ApprovalStepClass(string status)
    {
        if (status.Equals("Approved", StringComparison.OrdinalIgnoreCase)) return "done";
        if (status.Equals("Current", StringComparison.OrdinalIgnoreCase)) return "current";
        if (status.Equals("Rejected", StringComparison.OrdinalIgnoreCase) || status.Equals("Returned", StringComparison.OrdinalIgnoreCase)) return "blocked";
        if (status.Equals("WaitingRevision", StringComparison.OrdinalIgnoreCase)) return "revision";
        return "waiting";
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
        if (status.Equals("Pending", StringComparison.OrdinalIgnoreCase) || status.Equals("Open", StringComparison.OrdinalIgnoreCase) || status.Equals("Draft", StringComparison.OrdinalIgnoreCase)) return "pending";
        if (status.Equals("Rejected", StringComparison.OrdinalIgnoreCase) || status.Equals("Closed", StringComparison.OrdinalIgnoreCase) || status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) || status.Equals("Returned", StringComparison.OrdinalIgnoreCase)) return "danger";
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

    public class EmployeePortalFullProfile
    {
        public string FirstNameEn { get; set; } = string.Empty;
        public string SecondNameEn { get; set; } = string.Empty;
        public string ThirdNameEn { get; set; } = string.Empty;
        public string LastNameEn { get; set; } = string.Empty;
        public string PassportNo { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string MaritalStatus { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string Nationality { get; set; } = string.Empty;
        public string Religion { get; set; } = string.Empty;
        public string MotherCountry { get; set; } = string.Empty;
        public string MotherCity { get; set; } = string.Empty;
        public string PersonalEmail { get; set; } = string.Empty;
        public string PhoneExtension { get; set; } = string.Empty;
        public DateTime? JoiningDate { get; set; }
        public string WorkType { get; set; } = string.Empty;
        public string JobGrade { get; set; } = string.Empty;
        public string ContractType { get; set; } = string.Empty;
        public DateTime? ContractEndDate { get; set; }
        public string EmploymentStatus { get; set; } = string.Empty;
        public EmployeePortalFinancialProfile Financial { get; set; } = new();
        public EmployeePortalContractProfile Contract { get; set; } = new();
        public List<EmployeePortalIdentityDocument> IdentityDocuments { get; set; } = new();
        public List<EmployeePortalDocument> Documents { get; set; } = new();
        public List<EmployeePortalDependent> Dependents { get; set; } = new();
        public List<EmployeePortalProfileRecord> Records { get; set; } = new();

        public string FullNameEn => string.Join(" ", new[] { FirstNameEn, SecondNameEn, ThirdNameEn, LastNameEn }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    public class EmployeePortalIdentityDocument
    {
        public string DocumentType { get; set; } = string.Empty;
        public string DocumentNumber { get; set; } = string.Empty;
        public string NationalNumber { get; set; } = string.Empty;
        public string FamilyNumber { get; set; } = string.Empty;
        public DateTime? IssueDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string IssuingAuthority { get; set; } = string.Empty;
        public string PlaceOfIssue { get; set; } = string.Empty;
        public string VerificationStatus { get; set; } = string.Empty;
        public string TypeLabel => DocumentType.Equals("NationalId", StringComparison.OrdinalIgnoreCase)
            ? "البطاقة الوطنية"
            : DocumentType.Equals("Passport", StringComparison.OrdinalIgnoreCase) ? "جواز السفر" : DocumentType;
    }

    public class EmployeePortalDocument
    {
        public int Id { get; set; }
        public string DocumentType { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string StoredPath { get; set; } = string.Empty;
        public DateTime? ExpiryDate { get; set; }
        public string Notes { get; set; } = string.Empty;
        public DateTime? UploadedAt { get; set; }
    }

    public class EmployeePortalFinancialProfile
    {
        public string Currency { get; set; } = string.Empty;
        public string SalaryScale { get; set; } = string.Empty;
        public decimal BasicSalary { get; set; }
        public decimal DailySalary { get; set; }
        public decimal HourlyRate { get; set; }
        public string SocialSecurityType { get; set; } = string.Empty;
        public string SocialSecurityNo { get; set; } = string.Empty;
        public DateTime? SocialSecurityJoinDate { get; set; }
        public string TaxFile { get; set; } = string.Empty;
        public string TaxNo { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string BankBranch { get; set; } = string.Empty;
        public string UnitNo { get; set; } = string.Empty;
        public string Iban { get; set; } = string.Empty;
        public string CardNo { get; set; } = string.Empty;
    }

    public class EmployeePortalContractProfile
    {
        public string ContractNo { get; set; } = string.Empty;
        public string ContractType { get; set; } = string.Empty;
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public bool IsCurrent { get; set; }
        public string Note { get; set; } = string.Empty;
    }

    public class EmployeePortalDependent
    {
        public int Relation { get; set; }
        public string Name { get; set; } = string.Empty;
        public string NameOther { get; set; } = string.Empty;
        public DateTime? BirthDate { get; set; }
        public string Gender { get; set; } = string.Empty;
        public string Nationality { get; set; } = string.Empty;
        public bool IsEmergencyContact { get; set; }
        public bool IsDependent { get; set; }
        public string MobilePhone { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public string RelationLabel => Relation switch
        {
            1 => "الزوج/الزوجة",
            2 => "ابن",
            3 => "ابنة",
            _ => "قريب"
        };
    }

    public class EmployeePortalProfileRecord
    {
        public int RecordType { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string RefNo { get; set; } = string.Empty;
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public decimal Amount { get; set; }
        public bool IsCurrent { get; set; }
        public string Gpa { get; set; } = string.Empty;
        public string RefContactName { get; set; } = string.Empty;
        public string RefContactPosition { get; set; } = string.Empty;
        public string RefContactPhone { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public string TypeLabel => RecordType switch
        {
            1 => "التعليم", 2 => "الخبرة", 3 => "الشهادات", 4 => "الدورات",
            5 => "الطبي", 6 => "العهد", 7 => "العنوان", 8 => "جهة الطوارئ",
            9 => "الإقامة", 10 => "المهارات", 11 => "اللغات", 12 => "الملخص المهني",
            _ => "سجل"
        };
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
        public int? RequestTypeId { get; set; }
        public string RequestType { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }
        public DateTime? ActualCheckIn { get; set; }
        public DateTime? ActualCheckOut { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CurrentStep { get; set; } = string.Empty;
        public string ReviewNote { get; set; } = string.Empty;
        public ApprovalWorkflowEngine.FlowState? ApprovalFlow { get; set; }
        public bool IsOvertime { get; set; }
        public decimal RequestedOvertimeHours { get; set; }
        public decimal ActualAttendanceOvertimeHours { get; set; }
        public decimal ApprovedOvertimeHours { get; set; }
        public bool PayrollEligible { get; set; }
        public bool PayrollPosted { get; set; }
        public string PayrollStatus { get; set; } = string.Empty;
        public decimal? PayrollRateFactor { get; set; }
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

