using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Application.Announcements.Services;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Api;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Controllers.Api;

/// <summary>
/// واجهة الموظف بالموبايل (الأساسيات): ملف الموظف، الحضور الأخير، رصيد الإجازات،
/// الطلبات (عرض/تقديم)، البصم الذاتي، وطلب البصمة المفقودة. كلها مقيّدة بالموظف
/// صاحب التوكن. تعيد استخدام مخازن HRMS نفسها المستخدمة بالبوابة.
/// </summary>
[ApiController]
[Route("api/v1/me")]
[Route("api/me")]
[Authorize(AuthenticationSchemes = ApiTokenAuthHandler.SchemeName)]
public sealed class MeController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IProtectedFileService _protectedFiles;
    private readonly IAnnouncementService _announcements;

    public MeController(
        ApplicationDbContext db,
        IProtectedFileService protectedFiles,
        IAnnouncementService announcements)
    {
        _db = db;
        _protectedFiles = protectedFiles;
        _announcements = announcements;
    }

    private int EmployeeId =>
        int.TryParse(User.FindFirst("EmployeeId")?.Value, out var id) ? id : 0;

    private IActionResult? RequireEmployee() =>
        EmployeeId <= 0 ? BadRequest(new { message = "الحساب غير مرتبط بموظف." }) : null;

    /// <summary>ملف الموظف الأساسي.</summary>
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        if (RequireEmployee() is { } bad) return bad;
        var row = (await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT TOP 1 e.EmployeeNo, e.FullName, ISNULL(e.Position, N'') AS Position,
       ISNULL(d.Name, N'') AS Department, ISNULL(b.Name, N'') AS Branch,
       ISNULL(e.Phone, N'') AS Phone, ISNULL(e.Email, N'') AS Email,
       ISNULL(e.NationalId, N'') AS NationalId, e.HireDate, ISNULL(e.IsActive,1) AS IsActive
FROM Employees e
LEFT JOIN Departments d ON d.Id = e.DepartmentId
LEFT JOIN Branches b ON b.Id = e.BranchId
WHERE e.Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", EmployeeId),
            reader => new
            {
                employeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                fullName = HrmsDatabase.GetString(reader, "FullName"),
                position = HrmsDatabase.GetString(reader, "Position"),
                department = HrmsDatabase.GetString(reader, "Department"),
                branch = HrmsDatabase.GetString(reader, "Branch"),
                phone = HrmsDatabase.GetString(reader, "Phone"),
                email = HrmsDatabase.GetString(reader, "Email"),
                nationalId = HrmsDatabase.GetString(reader, "NationalId"),
                hireDate = HrmsDatabase.GetDateOnly(reader, "HireDate")?.ToString("yyyy-MM-dd"),
                isActive = HrmsDatabase.GetBool(reader, "IsActive")
            })).FirstOrDefault();

        return row is null ? NotFound(new { message = "الموظف غير موجود." }) : Ok(row);
    }

    /// <summary>آخر إعلانات الموظف كما تظهر في بوابة الموظف.</summary>
    [HttpGet("announcements")]
    public async Task<IActionResult> Announcements([FromQuery] int take = 5)
    {
        if (RequireEmployee() is { } bad) return bad;
        take = Math.Clamp(take, 1, 20);

        var items = await _announcements.GetEmployeeFeedAsync(
            EmployeeId,
            HttpContext.RequestAborted);

        return Ok(items
            .Take(take)
            .Select(item => new
            {
                id = item.Id,
                title = item.Title,
                body = item.Body,
                category = item.Category,
                publishDate = item.PublishDate?.ToString("yyyy-MM-dd"),
                isRead = item.IsRead,
                firstReadAtUtc = item.FirstReadAtUtc
            }));
    }

    /// <summary>بيانات التعويضات المالية للموظف الحالي فقط.</summary>
    [HttpGet("compensation")]
    public async Task<IActionResult> Compensation()
    {
        if (RequireEmployee() is { } bad) return bad;

        var row = (await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT TOP 1
    ISNULL(BasicSalary, 0) AS BasicSalary,
    ISNULL(Allowances, 0) AS Allowances,
    ISNULL(Deductions, 0) AS Deductions,
    ISNULL(PaymentMethod, N'') AS PaymentMethod,
    ISNULL(BankName, N'') AS BankName,
    ISNULL(BankAccount, N'') AS BankAccount,
    ISNULL(Currency, N'IQD') AS Currency
FROM EmployeeCompensations
WHERE EmployeeId = @EmployeeId
ORDER BY UpdatedAt DESC, Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", EmployeeId),
            reader => new
            {
                basicSalary = Convert.ToDecimal(reader["BasicSalary"]),
                allowances = Convert.ToDecimal(reader["Allowances"]),
                deductions = Convert.ToDecimal(reader["Deductions"]),
                paymentMethod = HrmsDatabase.GetString(reader, "PaymentMethod"),
                bankName = HrmsDatabase.GetString(reader, "BankName"),
                bankAccount = HrmsDatabase.GetString(reader, "BankAccount"),
                currency = HrmsDatabase.GetString(reader, "Currency")
            })).FirstOrDefault();

        if (row is null)
        {
            return Ok(new
            {
                hasData = false,
                basicSalary = 0m,
                allowances = 0m,
                deductions = 0m,
                net = 0m,
                paymentMethod = "",
                bankName = "",
                bankAccount = "",
                currency = "IQD"
            });
        }

        return Ok(new
        {
            hasData = true,
            row.basicSalary,
            row.allowances,
            row.deductions,
            net = row.basicSalary + row.allowances - row.deductions,
            row.paymentMethod,
            row.bankName,
            row.bankAccount,
            row.currency
        });
    }

    /// <summary>الحضور اليومي الأخير (من يوميات المحرك الرسمي).</summary>
    [HttpGet("attendance")]
    public async Task<IActionResult> Attendance([FromQuery] int days = 30)
    {
        if (RequireEmployee() is { } bad) return bad;
        days = Math.Clamp(days, 1, 90);
        var to = DateOnly.FromDateTime(DateTime.Today);
        var from = to.AddDays(-days);
        var mine = (await DayAttendanceStore.ListForEmployeeAsync(_db, EmployeeId, from, to))
            .OrderByDescending(r => r.WorkDate)
            .Select(r => new
            {
                date = r.WorkDate.ToString("yyyy-MM-dd"),
                status = DayAttendanceStore.StatusLabel(r.Status),
                checkIn = r.CheckIn?.ToString("HH:mm"),
                checkOut = r.CheckOut?.ToString("HH:mm"),
                lateHours = r.LateHours,
                workedHours = r.WorkedHours
            }).ToList();
        return Ok(mine);
    }

    /// <summary>أرصدة الإجازات/المغادرات ذات الرصيد من سياسة الشركة الحالية.</summary>
    [HttpGet("leave-balance")]
    public async Task<IActionResult> LeaveBalance()
    {
        if (RequireEmployee() is { } bad) return bad;
        var balances = await CompanyLeavePolicyStore.GetBalanceSnapshotsAsync(
            _db, EmployeeId, DateOnly.FromDateTime(DateTime.Today));

        return Ok(balances.Select(b => new
        {
            requestTypeId = b.SourceRequestTypeId,
            type = b.RequestTypeName,
            category = b.CategoryName,
            unit = b.Unit,
            entitled = b.Entitlement,
            approved = b.Approved,
            pendingReserved = b.PendingReserved,
            reserved = b.Reserved,
            used = b.Reserved,
            remaining = b.Remaining
        }));
    }

    /// <summary>كتالوج أنواع الطلبات الفعّالة المسموح بها للمستخدم الحالي.</summary>
    [HttpGet("request-types")]
    public async Task<IActionResult> RequestTypes()
    {
        if (RequireEmployee() is { } bad) return bad;

        var eligibility = await EmployeeRequestEligibility.CheckAsync(
            _db, EmployeeId, HttpContext.RequestAborted);
        if (!eligibility.IsEligible)
            return Ok(new
            {
                eligible = false,
                message = eligibility.Message,
                canSubmitMissingPunch = false,
                items = Array.Empty<object>()
            });

        var canSubmitMissingPunch = await SelfServiceAccessPolicy.IsAllowedAsync(
            _db, HttpContext, "PunchCorrection");

        await RequestTypeStore.EnsureAsync(_db);
        var types = await RequestTypeStore.ListTypesAsync(_db, onlyActive: true);
        var companyId = await HrmsDatabase.ScalarAsync<int>(
            _db,
            "SELECT ISNULL(CompanyId,0) FROM Employees WHERE Id=@Id AND ISNULL(IsDeleted,0)=0;",
            command => HrmsDatabase.AddParameter(command, "@Id", EmployeeId));
        var policyByType = companyId > 0
            ? (await CompanyLeavePolicyStore.ListForCompanyAsync(_db, companyId, onlyActive: true))
                .ToDictionary(policy => policy.RequestTypeId)
            : new Dictionary<int, CompanyLeavePolicyStore.Policy>();
        var items = new List<object>();

        foreach (var type in types)
        {
            var action = ActionForRequestType(type);
            if (action is null ||
                !await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, action))
                continue;

            policyByType.TryGetValue(type.Id, out var policy);
            var attachmentRequired =
                policy?.AttachmentRequiredOverride ?? type.AttachmentRequired;

            items.Add(new
            {
                id = type.Id,
                name = type.Name,
                nameEn = type.NameEn,
                category = type.CategoryName,
                needsTime = type.NeedsTime,
                attachmentRequired,
                attachmentLabel = type.AttachmentLabel,
                reasonRequired = policy?.ReasonRequired ?? false,
                allowedDays = type.AllowedDays,
                effectCode = type.EffectCode,
                hasBalance = type.HasBalance
            });
        }

        return Ok(new
        {
            eligible = true,
            message = (string?)null,
            canSubmitMissingPunch,
            items
        });
    }

    /// <summary>طلبات البصمة المفقودة الخاصة بي.</summary>
    [HttpGet("missing-punch")]
    public async Task<IActionResult> MyMissingPunches()
    {
        if (RequireEmployee() is { } bad) return bad;
        var rows = await MissingPunchRequestStore.ListAsync(_db, SmartAttendance.Web.Infrastructure.Security.CompanyScope.Unrestricted(), new MissingPunchRequestStore.Filter { EmployeeId = EmployeeId });
        return Ok(rows.Select(r => new
        {
            refNo = r.RefNo,
            punchAt = r.PunchAt.ToString("yyyy-MM-dd HH:mm"),
            punchType = r.PunchTypeText,
            status = r.StatusText,
            reason = r.Reason
        }));
    }

    public sealed record OnlinePunchRequest(string PunchType, double? Latitude = null, double? Longitude = null);

    /// <summary>بصمة ذاتية (دخول/خروج) بوقت الخادم الحالي.</summary>
    [HttpPost("online-punch")]
    public async Task<IActionResult> OnlinePunch([FromBody] OnlinePunchRequest body)
    {
        if (RequireEmployee() is { } bad) return bad;
        var type = body?.PunchType == "Out" ? "Out" : "In";
        var now = DateTime.Now;
        var result = await OnlinePunchStore.RecordAsync(_db, EmployeeId, type, now, null, body?.Latitude, body?.Longitude);
        return result.Status switch
        {
            OnlinePunchStore.PunchStatus.OutsideGeofence =>
                BadRequest(new { message = "رُفضت البصمة: أنت خارج نطاق موقع العمل المحدد لك (أو لم يصل موقعك)." }),
            OnlinePunchStore.PunchStatus.Recorded =>
                Ok(new { message = $"سُجّلت بصمة {(type == "Out" ? "الانصراف" : "الحضور")}.", at = now.ToString("yyyy-MM-dd HH:mm"), punchType = type }),
            OnlinePunchStore.PunchStatus.TooSoonForCheckout =>
                BadRequest(new { message = $"لا يمكن تسجيل الانصراف قبل مرور {OnlinePunchStore.FormatDuration(result.MinCheckoutHours)} من تسجيل الحضور — تبقّى {OnlinePunchStore.FormatDuration(result.HoursRemaining)}." }),
            _ =>
                BadRequest(new { message = "تم تجاهل البصمة: سُجّلت بصمة مماثلة خلال أقل من دقيقة." })
        };
    }

    /// <summary>بصمات يوم الموظف مصنَّفةً بالأسبقية (دخول/خروج) — لمعاينة نموذج نسيان البصمة.</summary>
    [HttpGet("punches")]
    public async Task<IActionResult> DayPunches([FromQuery] string date)
    {
        if (RequireEmployee() is { } bad) return bad;
        if (!DateOnly.TryParse(date, out var d))
            return BadRequest(new { message = "تاريخ غير صالح." });

        var times = await PunchTypingEngine.DayPunchTimesAsync(_db, EmployeeId, d);
        var typed = PunchTypingEngine.Derive(times);
        return Ok(typed.Select(p => new { at = p.At.ToString("HH:mm"), type = p.Type, typeText = p.TypeLabel }));
    }

    // PunchType لم يعد يُرسله العميل — يُشتَق تلقائياً بالأسبقية الزمنية.
    public sealed record MissingPunchRequestBody(string Date, string Time, string? Reason);

    /// <summary>تقديم طلب بصمة مفقودة (يصل لصفحة إدارة الطلبات بمصدر «خدمة ذاتية»).</summary>
    [HttpPost("missing-punch")]
    public async Task<IActionResult> SubmitMissingPunch([FromBody] MissingPunchRequestBody body)
    {
        if (RequireEmployee() is { } bad) return bad;

        var eligibility = await EmployeeRequestEligibility.CheckAsync(
            _db, EmployeeId, HttpContext.RequestAborted);
        if (!eligibility.IsEligible)
            return BadRequest(new { message = eligibility.Message });

        if (!await SelfServiceAccessPolicy.IsAllowedAsync(
                _db, HttpContext, "PunchCorrection"))
            return Forbid();

        if (body is null || !DateOnly.TryParse(body.Date, out var d) || !TimeOnly.TryParse(body.Time, out var t))
            return BadRequest(new { message = "أدخل تاريخ ووقت البصمة." });

        var punchAt = d.ToDateTime(t);
        // النوع يُشتَق بالأسبقية الزمنية بين بصمات اليوم والبصمة المُضافة (لا اختيار يدوي).
        var existingTimes = await PunchTypingEngine.DayPunchTimesAsync(_db, EmployeeId, d);
        var derivedType = PunchTypingEngine.DeriveTypeFor(existingTimes, punchAt);

        var (ok, message) = await MissingPunchRequestStore.SaveAsync(
            _db, SmartAttendance.Web.Infrastructure.Security.CompanyScope.Unrestricted(), new MissingPunchRequestStore.Request
        {
            EmployeeId = EmployeeId,
            PunchAt = punchAt,
            PunchType = derivedType,
            Reason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim(),
            Source = "خدمة ذاتية"
        }, User.Identity?.Name ?? "employee", EmployeeId);

        return ok ? Ok(new { message, derivedType }) : BadRequest(new { message });
    }

    /// <summary>طلبات الخدمة الذاتية العامة الخاصة بي (إجازة/مغادرة/...).</summary>
    [HttpGet("requests")]
    public async Task<IActionResult> MyRequests()
    {
        if (RequireEmployee() is { } bad) return bad;
        var rows = await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT TOP 100 Id,RequestTypeId,RequestType,FromDate,ToDate,StartTime,EndTime,
       Reason,Status,CreatedAt,
       CASE WHEN NULLIF(LTRIM(RTRIM(ISNULL(AttachmentPath,N''))),N'') IS NULL
            THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS HasAttachment
FROM SelfServiceRequests WHERE EmployeeId = @Id ORDER BY CreatedAt DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", EmployeeId),
            reader => new
            {
                id = HrmsDatabase.GetInt(reader,"Id"),
                requestTypeId = HrmsDatabase.GetNullableInt(reader, "RequestTypeId"),
                type = HrmsDatabase.GetString(reader, "RequestType"),
                fromDate = HrmsDatabase.GetDateTime(reader, "FromDate")?.ToString("yyyy-MM-dd"),
                toDate = HrmsDatabase.GetDateTime(reader, "ToDate")?.ToString("yyyy-MM-dd"),
                startTime = HrmsDatabase.GetTimeSpan(reader, "StartTime")?.ToString(@"hh\:mm"),
                endTime = HrmsDatabase.GetTimeSpan(reader, "EndTime")?.ToString(@"hh\:mm"),
                reason = HrmsDatabase.GetString(reader, "Reason"),
                status = HrmsDatabase.GetString(reader, "Status"),
                hasAttachment = HrmsDatabase.GetBool(reader, "HasAttachment"),
                createdAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")?.ToString("yyyy-MM-dd HH:mm")
            });
        return Ok(rows);
    }

    public sealed record SelfServiceRequestBody(string RequestType, string FromDate, string? ToDate, string? Reason);

    /// <summary>
    /// المسار القديم محفوظ فقط لمنع نسخ 0.3.0 من إنشاء طلبات ناقصة بلا هوية نوع/وقت/مرفق.
    /// </summary>
    [HttpPost("requests")]
    public IActionResult SubmitLegacyRequest([FromBody] SelfServiceRequestBody body) =>
        BadRequest(new
        {
            message = "هذه النسخة من تطبيق ZYNORA قديمة لإرسال الطلبات. حدّث التطبيق ثم أعد المحاولة."
        });

    public sealed class MobileRequestForm
    {
        public int RequestTypeId { get; set; }
        public string FromDate { get; set; } = string.Empty;
        public string? ToDate { get; set; }
        public string? StartTime { get; set; }
        public string? EndTime { get; set; }
        public string? Reason { get; set; }
        public IFormFile? Attachment { get; set; }
    }

    /// <summary>إنشاء طلب موبايل مُهيكل من كتالوج الشركة الفعّال.</summary>
    [HttpPost("requests/create")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> CreateRequest([FromForm] MobileRequestForm form)
    {
        if (RequireEmployee() is { } bad) return bad;

        var eligibility = await EmployeeRequestEligibility.CheckAsync(
            _db, EmployeeId, HttpContext.RequestAborted);
        if (!eligibility.IsEligible)
            return BadRequest(new { message = eligibility.Message });

        if (form is null || form.RequestTypeId <= 0)
            return BadRequest(new { message = "اختر نوع الطلب." });

        await RequestTypeStore.EnsureAsync(_db);
        var type = await RequestTypeStore.GetTypeAsync(_db, form.RequestTypeId);
        if (type is null || !type.IsActive)
            return BadRequest(new { message = "نوع الطلب غير متاح حالياً." });

        var action = ActionForRequestType(type);
        if (action is null ||
            !await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, action))
            return Forbid();

        if (!DateOnly.TryParse(form.FromDate, out var from))
            return BadRequest(new { message = "تاريخ البداية مطلوب." });

        var to = DateOnly.TryParse(form.ToDate, out var parsedTo) ? parsedTo : from;
        if (to < from)
            return BadRequest(new { message = "تاريخ النهاية قبل البداية." });

        TimeSpan? startTime = null;
        TimeSpan? endTime = null;
        if (type.NeedsTime)
        {
            if (!TimeSpan.TryParse(form.StartTime, out var parsedStart) ||
                !TimeSpan.TryParse(form.EndTime, out var parsedEnd))
                return BadRequest(new { message = "وقت البداية والنهاية مطلوبان لهذا النوع." });

            startTime = parsedStart;
            endTime = parsedEnd;
            if (to == from && parsedEnd <= parsedStart)
                to = from.AddDays(1);

            var incomplete = await FindIncompletePunchDayAsync(EmployeeId, from, to);
            if (incomplete is { } missingDate)
                return BadRequest(new
                {
                    message = $"لا يمكن تقديم طلب زمني قبل معالجة البصمة الناقصة ليوم {missingDate:yyyy-MM-dd}."
                });
        }

        var hasAttachment = form.Attachment is { Length: > 0 };

        var days = to.DayNumber - from.DayNumber + 1;
        if (type.AllowedDays is int maxDays && days > maxDays)
            return BadRequest(new { message = $"عدد الأيام يتجاوز المسموح ({maxDays} يوم)." });

        var reason = string.IsNullOrWhiteSpace(form.Reason)
            ? "تم الإرسال من تطبيق الموبايل"
            : form.Reason.Trim();

        var policy = await CompanyLeavePolicyStore.ValidateRequestAsync(
            _db, EmployeeId, 0, type.Id, type.Name,
            from, to, startTime, endTime, reason, hasAttachment);
        if (!policy.Ok)
            return BadRequest(new { message = policy.Message });

        string? attachmentPath = null;
        if (hasAttachment)
        {
            attachmentPath = await _protectedFiles.SaveAsync(
                form.Attachment, EmployeeId, "request", HttpContext.RequestAborted);
            if (attachmentPath is null)
                return BadRequest(new
                {
                    message = "تعذر قبول المرفق. استخدم ملفاً مدعوماً وبحجم مسموح ثم أعد المحاولة."
                });
        }

        var requestId = await HrmsDatabase.ScalarAsync<int>(
            _db,
            """
INSERT INTO SelfServiceRequests
(EmployeeId,RequestTypeId,RequestType,CreatedAt,FromDate,ToDate,StartTime,EndTime,
 Reason,Status,DaysCount,AttachmentPath,RequestSource)
VALUES
(@Emp,@TypeId,@Type,SYSUTCDATETIME(),@From,@To,@Start,@End,
 @Reason,'Pending',@Days,@Attachment,N'SelfService');
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Emp", EmployeeId);
                HrmsDatabase.AddParameter(command, "@TypeId", type.Id);
                HrmsDatabase.AddParameter(command, "@Type", type.Name);
                HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", to.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@Start", (object?)startTime ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@End", (object?)endTime ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Reason", reason);
                HrmsDatabase.AddParameter(command, "@Days", (decimal)days);
                HrmsDatabase.AddParameter(command, "@Attachment", (object?)attachmentPath ?? DBNull.Value);
            });

        var start = await ApprovalWorkflowEngine.StartAsync(
            _db, requestId, type.Name, EmployeeId);
        if (!start.Ok)
            return BadRequest(new { message = start.Message, requestId });

        return Ok(new
        {
            message = $"تم إرسال طلب {type.Name} وهو قيد المراجعة.",
            requestId
        });
    }

    public sealed record CancelRequestBody(string? Reason);

    /// <summary>إلغاء طلب الموظف نفسه ضمن مهلة القالب المجمدة وقت تقديمه.</summary>
    [HttpPost("requests/{requestId:int}/cancel")]
    public async Task<IActionResult> CancelRequest(int requestId,[FromBody] CancelRequestBody? body)
    {
        if(RequireEmployee() is { } bad) return bad;
        var result=await ApprovalWorkflowEngine.CancelByRequesterAsync(
            _db,requestId,EmployeeId,User.Identity?.Name ?? EmployeeId.ToString(),body?.Reason);
        return result.Ok ? Ok(new { message=result.Message }) : BadRequest(new { message=result.Message });
    }

    /// <summary>الحقول القابلة لطلب تعديلها + قيمتها الحالية وخيارات القوائم (لبناء نموذج «تعديل بياناتي»).</summary>
    [HttpGet("data-change/fields")]
    public async Task<IActionResult> DataChangeFields()
    {
        if (RequireEmployee() is { } bad) return bad;
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, "UpdateMyData"))
            return Forbid();

        var fields = await DataChangeRequestStore.ListEditableAsync(_db, EmployeeId);
        var result = new List<object>();

        foreach (var field in fields)
        {
            var options = field.Kind == "select"
                ? await DataChangeRequestStore.OptionsAsync(_db, field.OptionsKey)
                : new List<DataChangeRequestStore.Option>();

            result.Add(new
            {
                key = field.Key,
                label = field.Label,
                currentValue = field.OldValue,
                kind = field.Kind,
                options = options.Select(option => new
                {
                    value = option.Value,
                    label = option.Label
                }).ToArray()
            });
        }

        return Ok(result);
    }

    public sealed record DataChangeItem(string Key, string? NewValue);
    public sealed record DataChangeBody(List<DataChangeItem> Fields, string? Reason);

    /// <summary>تقديم طلب تعديل بيانات (لا يُطبَّق إلا بعد اعتماد لجنة الموافقة).</summary>
    [HttpPost("data-change")]
    public async Task<IActionResult> SubmitDataChange([FromBody] DataChangeBody body)
    {
        if (RequireEmployee() is { } bad) return bad;
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, "UpdateMyData"))
            return Forbid();
        if (body?.Fields is not { Count: > 0 })
            return BadRequest(new { message = "اختر حقلاً واحداً على الأقل لتعديله." });

        // القيم الحالية للتحقق من وجود تغيير فعلي.
        var editable = await DataChangeRequestStore.ListEditableAsync(_db, EmployeeId);
        var currentByKey = editable.ToDictionary(f => f.Key, f => f, StringComparer.OrdinalIgnoreCase);

        var proposed = new List<DataChangeRequestStore.ProposedField>();
        foreach (var item in body.Fields)
        {
            if (!currentByKey.TryGetValue(item.Key ?? "", out var def)) continue;
            proposed.Add(new DataChangeRequestStore.ProposedField
            {
                Key = def.Key,
                OldValue = def.OldValue,
                NewValue = item.NewValue
            });
        }
        if (proposed.Count == 0)
            return BadRequest(new { message = "لا توجد حقول صالحة للتعديل." });

        var reason = string.IsNullOrWhiteSpace(body.Reason) ? "طلب تعديل بيانات من تطبيق الموبايل" : body.Reason!.Trim();

        var requestId = await HrmsDatabase.ScalarAsync<int>(
            _db,
            """
INSERT INTO SelfServiceRequests (EmployeeId, RequestType, CreatedAt, Reason, Status, RequestSource)
VALUES (@Emp, @Type, SYSUTCDATETIME(), @Reason, 'Pending', N'SelfService');
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Emp", EmployeeId);
                HrmsDatabase.AddParameter(command, "@Type", DataChangeRequestStore.RequestTypeLabel);
                HrmsDatabase.AddParameter(command, "@Reason", reason);
            });

        if (requestId <= 0)
            return BadRequest(new { message = "تعذّر إنشاء الطلب." });

        var savedCount = await DataChangeRequestStore.SaveFieldsAsync(_db, requestId, proposed);
        if (savedCount == 0)
        {
            await HrmsDatabase.ExecuteAsync(_db, "DELETE FROM SelfServiceRequests WHERE Id=@r",
                cmd => HrmsDatabase.AddParameter(cmd, "@r", requestId));
            return BadRequest(new { message = "لم تُدخِل أي قيمة مختلفة عن الحالية." });
        }

        var start=await ApprovalWorkflowEngine.StartAsync(_db, requestId, DataChangeRequestStore.RequestTypeLabel, EmployeeId);
        if(!start.Ok) return BadRequest(new { message=start.Message, requestId });
        return Ok(new { message = $"تم إرسال طلب تعديل البيانات ({savedCount} حقل) وهو قيد المراجعة.", requestId });
    }

    /// <summary>نسخة Multipart للموبايل: تعديل حقول + صورة شخصية ضمن نفس طلب الموافقة.</summary>
    [HttpPost("data-change/multipart")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SubmitDataChangeMultipart(
        [FromForm] string? FieldsJson,
        [FromForm] string? Reason,
        [FromForm] IFormFile? Photo,
        [FromServices] IWebHostEnvironment environment)
    {
        if (RequireEmployee() is { } bad) return bad;
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, "UpdateMyData"))
            return Forbid();

        var eligibility = await EmployeeRequestEligibility.CheckAsync(
            _db, EmployeeId, HttpContext.RequestAborted);
        if (!eligibility.IsEligible)
            return BadRequest(new { message = eligibility.Message });

        List<DataChangeItem> items;
        try
        {
            items = string.IsNullOrWhiteSpace(FieldsJson)
                ? new List<DataChangeItem>()
                : System.Text.Json.JsonSerializer.Deserialize<List<DataChangeItem>>(
                    FieldsJson,
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
                  ?? new List<DataChangeItem>();
        }
        catch
        {
            return BadRequest(new { message = "بيانات التعديل غير صالحة." });
        }

        var editable = await DataChangeRequestStore.ListEditableAsync(_db, EmployeeId);
        var currentByKey = editable.ToDictionary(field => field.Key, StringComparer.OrdinalIgnoreCase);
        var proposed = new List<DataChangeRequestStore.ProposedField>();

        foreach (var item in items)
        {
            if (!currentByKey.TryGetValue(item.Key ?? "", out var def) ||
                string.Equals(def.Kind, "photo", StringComparison.OrdinalIgnoreCase))
                continue;

            var newValue = item.NewValue?.Trim();
            if (string.IsNullOrWhiteSpace(newValue))
                continue;

            if (string.Equals(def.Kind, "select", StringComparison.OrdinalIgnoreCase))
            {
                var options = await DataChangeRequestStore.OptionsAsync(_db, def.OptionsKey);
                if (!options.Any(option =>
                        string.Equals(option.Value, newValue, StringComparison.OrdinalIgnoreCase)))
                    continue;
            }

            proposed.Add(new DataChangeRequestStore.ProposedField
            {
                Key = def.Key,
                OldValue = def.OldValue,
                NewValue = newValue
            });
        }

        if (Photo is { Length: > 0 })
        {
            var extension = Path.GetExtension(Photo.FileName);
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".webp"
            };

            if (string.IsNullOrWhiteSpace(extension) || !allowed.Contains(extension))
                return BadRequest(new { message = "صيغة الصورة غير مدعومة (JPG/PNG/WEBP)." });
            if (Photo.Length > 5 * 1024 * 1024)
                return BadRequest(new { message = "حجم الصورة أكبر من 5MB." });
            if (!await UploadSignatureValidator.IsValidImageAsync(Photo))
                return BadRequest(new { message = "محتوى الملف ليس صورة صالحة." });

            var directory = Path.Combine(
                environment.WebRootPath,
                "uploads",
                "employee-photos");
            Directory.CreateDirectory(directory);

            var storedName =
                $"emp_{EmployeeId}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension.ToLowerInvariant()}";
            var physicalPath = Path.Combine(directory, storedName);
            await using (var stream = System.IO.File.Create(physicalPath))
                await Photo.CopyToAsync(stream, HttpContext.RequestAborted);

            currentByKey.TryGetValue("PhotoPath", out var photoDef);
            proposed.Add(new DataChangeRequestStore.ProposedField
            {
                Key = "PhotoPath",
                OldValue = photoDef?.OldValue,
                NewValue = $"/uploads/employee-photos/{storedName}"
            });
        }

        if (proposed.Count == 0)
            return BadRequest(new { message = "لم تُدخِل أي قيمة جديدة لتعديلها." });

        var normalizedReason = string.IsNullOrWhiteSpace(Reason)
            ? "طلب تعديل بيانات من تطبيق الموبايل"
            : Reason.Trim();

        var requestId = await HrmsDatabase.ScalarAsync<int>(
            _db,
            """
INSERT INTO SelfServiceRequests (EmployeeId, RequestType, CreatedAt, Reason, Status, RequestSource)
VALUES (@Emp, @Type, SYSUTCDATETIME(), @Reason, 'Pending', N'SelfService');
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Emp", EmployeeId);
                HrmsDatabase.AddParameter(command, "@Type", DataChangeRequestStore.RequestTypeLabel);
                HrmsDatabase.AddParameter(command, "@Reason", normalizedReason);
            });

        if (requestId <= 0)
            return BadRequest(new { message = "تعذّر إنشاء الطلب." });

        var savedCount = await DataChangeRequestStore.SaveFieldsAsync(_db, requestId, proposed);
        if (savedCount == 0)
        {
            await HrmsDatabase.ExecuteAsync(
                _db,
                "DELETE FROM SelfServiceRequests WHERE Id=@r",
                command => HrmsDatabase.AddParameter(command, "@r", requestId));
            return BadRequest(new { message = "لم تُدخِل أي قيمة مختلفة عن الحالية." });
        }

        var start = await ApprovalWorkflowEngine.StartAsync(
            _db,
            requestId,
            DataChangeRequestStore.RequestTypeLabel,
            EmployeeId);

        return start.Ok
            ? Ok(new
            {
                message = $"تم إرسال طلب تعديل البيانات ({savedCount} حقل) وهو قيد المراجعة.",
                requestId
            })
            : BadRequest(new { message = start.Message, requestId });
    }

    public sealed record FinancialRequestBody(
        string Kind,
        decimal Amount,
        int InstallmentCount,
        int StartYear,
        int StartMonth,
        string? Reason);

    /// <summary>كتالوج الطلبات المالية المتاحة للموظف نفسه.</summary>
    [HttpGet("financial/catalog")]
    public async Task<IActionResult> FinancialCatalog()
    {
        if (RequireEmployee() is { } bad) return bad;
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, "FinancialRequest"))
            return Forbid();

        var eligibility = await EmployeeRequestEligibility.CheckAsync(
            _db, EmployeeId, HttpContext.RequestAborted);

        return Ok(new
        {
            eligible = eligibility.IsEligible,
            message = eligibility.IsEligible ? (string?)null : eligibility.Message,
            items = FinancialRequestStore.Catalog
                .Where(kind => kind.Key != FinancialRequestStore.Raise)
                .Select(kind => new
                {
                    key = kind.Key,
                    label = kind.Label,
                    hint = kind.Hint
                })
                .ToArray()
        });
    }

    /// <summary>تقديم طلب مالي ذاتي (قرض/سلفة/بدل/استرداد).</summary>
    [HttpPost("financial")]
    public async Task<IActionResult> SubmitFinancial([FromBody] FinancialRequestBody body)
    {
        if (RequireEmployee() is { } bad) return bad;
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, "FinancialRequest"))
            return Forbid();

        var eligibility = await EmployeeRequestEligibility.CheckAsync(
            _db, EmployeeId, HttpContext.RequestAborted);
        if (!eligibility.IsEligible)
            return BadRequest(new { message = eligibility.Message });

        var kind = FinancialRequestStore.KindOf(body.Kind);
        if (kind is null || kind.Key == FinancialRequestStore.Raise)
            return BadRequest(new { message = "نوع الطلب المالي غير صالح." });
        if (body.Amount <= 0)
            return BadRequest(new { message = "المبلغ مطلوب ويجب أن يكون أكبر من صفر." });

        var today = DateTime.Today;
        var year = body.StartYear is >= 2000 and <= 2200 ? body.StartYear : today.Year;
        var month = body.StartMonth is >= 1 and <= 12 ? body.StartMonth : today.Month;

        var detail = new FinancialRequestStore.Detail
        {
            Kind = kind.Key,
            Amount = body.Amount,
            InstallmentCount = Math.Max(1, body.InstallmentCount),
            StartYear = year,
            StartMonth = month,
            PaymentType = kind.Key == FinancialRequestStore.Reimbursement ? "OutSalary" : "InSalary",
            Reason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim()
        };

        var requestId = await FinancialRequestStore.SubmitAsync(
            _db,
            detail,
            EmployeeId,
            User.Identity?.Name ?? EmployeeId.ToString(),
            "SelfService");

        return requestId > 0
            ? Ok(new
            {
                message = $"تم إرسال طلب {FinancialRequestStore.KindLabel(kind.Key)} وهو الآن قيد المراجعة.",
                requestId
            })
            : BadRequest(new { message = "تعذّر إرسال الطلب المالي." });
    }

    public sealed record ShiftRequestBody(
        int ShiftTypeId,
        DateOnly FromDate,
        DateOnly? ToDate,
        string? Reason);

    /// <summary>المناوبات المسموح بطلبها من الخدمة الذاتية.</summary>
    [HttpGet("shift/catalog")]
    public async Task<IActionResult> ShiftCatalog()
    {
        if (RequireEmployee() is { } bad) return bad;
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, "ShiftRequest"))
            return Forbid();

        var eligibility = await EmployeeRequestEligibility.CheckAsync(
            _db, EmployeeId, HttpContext.RequestAborted);

        var shifts = eligibility.IsEligible
            ? await ShiftRequestStore.RequestableShiftsAsync(_db)
            : new List<(int Id, string Name)>();

        return Ok(new
        {
            eligible = eligibility.IsEligible,
            message = eligibility.IsEligible ? (string?)null : eligibility.Message,
            items = shifts.Select(shift => new { id = shift.Id, name = shift.Name }).ToArray()
        });
    }

    /// <summary>تقديم طلب مناوبة ذاتي.</summary>
    [HttpPost("shift")]
    public async Task<IActionResult> SubmitShift([FromBody] ShiftRequestBody body)
    {
        if (RequireEmployee() is { } bad) return bad;
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, "ShiftRequest"))
            return Forbid();

        var eligibility = await EmployeeRequestEligibility.CheckAsync(
            _db, EmployeeId, HttpContext.RequestAborted);
        if (!eligibility.IsEligible)
            return BadRequest(new { message = eligibility.Message });
        if (body.ShiftTypeId <= 0)
            return BadRequest(new { message = "اختر المناوبة المطلوبة." });

        var to = body.ToDate ?? body.FromDate;
        var requestId = await ShiftRequestStore.SubmitAsync(
            _db,
            EmployeeId,
            body.ShiftTypeId,
            body.FromDate,
            to,
            string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim(),
            User.Identity?.Name ?? EmployeeId.ToString());

        return requestId > 0
            ? Ok(new { message = "تم إرسال طلب المناوبة وهو الآن قيد المراجعة.", requestId })
            : BadRequest(new { message = "تعذّر إرسال طلب المناوبة — تأكد أن المناوبة متاحة للطلب." });
    }

    private static string? ActionForRequestType(RequestTypeStore.ReqType type)
    {
        var effect = type.EffectCode?.Trim() ?? string.Empty;
        if (effect.Contains("Overtime", StringComparison.OrdinalIgnoreCase))
            return "OvertimeRequest";
        if (effect.Contains("ExitPermission", StringComparison.OrdinalIgnoreCase))
            return "ExitPermission";
        if (effect.Contains("MissingPunch", StringComparison.OrdinalIgnoreCase))
            return "PunchCorrection";
        if (effect.StartsWith("Leave", StringComparison.OrdinalIgnoreCase) ||
            effect.Equals("BusinessTrip", StringComparison.OrdinalIgnoreCase))
            return "LeaveRequest";

        return SelfServiceAccessPolicy.ActionForRequestType(type.Name);
    }

    private async Task<DateOnly?> FindIncompletePunchDayAsync(
        int employeeId, DateOnly from, DateOnly to)
    {
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var times = await PunchTypingEngine.DayPunchTimesAsync(_db, employeeId, day);
            if (times.Count % 2 != 0)
                return day;
        }

        return null;
    }
}
