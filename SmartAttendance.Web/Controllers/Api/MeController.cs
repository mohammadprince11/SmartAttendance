using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Api;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Controllers.Api;

/// <summary>
/// واجهة الموظف بالموبايل (الأساسيات): ملف الموظف، الحضور الأخير، رصيد الإجازات،
/// الطلبات (عرض/تقديم)، البصم الذاتي، وطلب البصمة المفقودة. كلها مقيّدة بالموظف
/// صاحب التوكن. تعيد استخدام مخازن HRMS نفسها المستخدمة بالبوابة.
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize(AuthenticationSchemes = ApiTokenAuthHandler.SchemeName)]
public sealed class MeController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public MeController(ApplicationDbContext db)
    {
        _db = db;
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

    /// <summary>الحضور اليومي الأخير (من يوميات المحرك الرسمي).</summary>
    [HttpGet("attendance")]
    public async Task<IActionResult> Attendance([FromQuery] int days = 30)
    {
        if (RequireEmployee() is { } bad) return bad;
        days = Math.Clamp(days, 1, 90);
        var to = DateOnly.FromDateTime(DateTime.Today);
        var from = to.AddDays(-days);
        var rows = await DayAttendanceStore.ListRangeAsync(_db, from, to, null);
        var mine = rows.Where(r => r.EmployeeId == EmployeeId)
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

    /// <summary>رصيد الإجازات (سنوي/مرضي) للسنة الحالية.</summary>
    [HttpGet("leave-balance")]
    public async Task<IActionResult> LeaveBalance()
    {
        if (RequireEmployee() is { } bad) return bad;
        var year = DateTime.Today.Year;
        var balances = await LeaveBalanceCalculator.ForEmployeeAsync(_db, EmployeeId, year);
        return Ok(balances.Select(b => new
        {
            type = b.Type.ToString(),
            entitled = b.Entitled + b.CarriedOver,
            used = b.Used,
            remaining = b.Remaining
        }));
    }

    /// <summary>طلبات البصمة المفقودة الخاصة بي.</summary>
    [HttpGet("missing-punch")]
    public async Task<IActionResult> MyMissingPunches()
    {
        if (RequireEmployee() is { } bad) return bad;
        var rows = await MissingPunchRequestStore.ListAsync(_db, new MissingPunchRequestStore.Filter { EmployeeId = EmployeeId });
        return Ok(rows.Select(r => new
        {
            refNo = r.RefNo,
            punchAt = r.PunchAt.ToString("yyyy-MM-dd HH:mm"),
            punchType = r.PunchTypeText,
            status = r.StatusText,
            reason = r.Reason
        }));
    }

    public sealed record OnlinePunchRequest(string PunchType);

    /// <summary>بصمة ذاتية (دخول/خروج) بوقت الخادم الحالي.</summary>
    [HttpPost("online-punch")]
    public async Task<IActionResult> OnlinePunch([FromBody] OnlinePunchRequest body)
    {
        if (RequireEmployee() is { } bad) return bad;
        var type = body?.PunchType == "Out" ? "Out" : "In";
        var now = DateTime.Now;
        var recordId = await OnlinePunchStore.RecordAsync(_db, EmployeeId, type, now, null);
        return recordId > 0
            ? Ok(new { message = $"سُجّلت بصمة {(type == "Out" ? "الانصراف" : "الحضور")}.", at = now.ToString("yyyy-MM-dd HH:mm"), punchType = type })
            : BadRequest(new { message = "تم تجاهل البصمة: سُجّلت بصمة مماثلة خلال أقل من دقيقة." });
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
        if (body is null || !DateOnly.TryParse(body.Date, out var d) || !TimeOnly.TryParse(body.Time, out var t))
            return BadRequest(new { message = "أدخل تاريخ ووقت البصمة." });

        var punchAt = d.ToDateTime(t);
        // النوع يُشتَق بالأسبقية الزمنية بين بصمات اليوم والبصمة المُضافة (لا اختيار يدوي).
        var existingTimes = await PunchTypingEngine.DayPunchTimesAsync(_db, EmployeeId, d);
        var derivedType = PunchTypingEngine.DeriveTypeFor(existingTimes, punchAt);

        var (ok, message) = await MissingPunchRequestStore.SaveAsync(_db, new MissingPunchRequestStore.Request
        {
            EmployeeId = EmployeeId,
            PunchAt = punchAt,
            PunchType = derivedType,
            Reason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason.Trim(),
            Source = "خدمة ذاتية"
        }, User.Identity?.Name ?? "employee");

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
SELECT TOP 100 RequestType, FromDate, ToDate, Reason, Status, CreatedAt
FROM SelfServiceRequests WHERE EmployeeId = @Id ORDER BY CreatedAt DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", EmployeeId),
            reader => new
            {
                type = HrmsDatabase.GetString(reader, "RequestType"),
                fromDate = HrmsDatabase.GetDateTime(reader, "FromDate")?.ToString("yyyy-MM-dd"),
                toDate = HrmsDatabase.GetDateTime(reader, "ToDate")?.ToString("yyyy-MM-dd"),
                reason = HrmsDatabase.GetString(reader, "Reason"),
                status = HrmsDatabase.GetString(reader, "Status"),
                createdAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")?.ToString("yyyy-MM-dd HH:mm")
            });
        return Ok(rows);
    }

    public sealed record SelfServiceRequestBody(string RequestType, string FromDate, string? ToDate, string? Reason);

    /// <summary>تقديم طلب خدمة ذاتية عام (إجازة/نسيان بصمة/خروج شخصي/خروج عمل/أوفر تايم).</summary>
    [HttpPost("requests")]
    public async Task<IActionResult> SubmitRequest([FromBody] SelfServiceRequestBody body)
    {
        if (RequireEmployee() is { } bad) return bad;

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "إجازة", "نسيان بصمة", "خروج شخصي", "خروج عمل", "أوفر تايم" };

        if (body is null || string.IsNullOrWhiteSpace(body.RequestType) || !allowed.Contains(body.RequestType.Trim()))
            return BadRequest(new { message = "نوع الطلب غير صالح." });
        if (!DateOnly.TryParse(body.FromDate, out var from))
            return BadRequest(new { message = "تاريخ البداية مطلوب." });

        DateOnly? to = DateOnly.TryParse(body.ToDate, out var t) ? t : null;
        if (to is { } tov && tov < from)
            return BadRequest(new { message = "تاريخ النهاية قبل البداية." });

        var reason = string.IsNullOrWhiteSpace(body.Reason) ? "تم الإرسال من تطبيق الموبايل" : body.Reason!.Trim();

        var requestId = await HrmsDatabase.ScalarAsync<int>(
            _db,
            """
INSERT INTO SelfServiceRequests (EmployeeId, RequestType, CreatedAt, FromDate, ToDate, Reason, Status)
VALUES (@Emp, @Type, SYSUTCDATETIME(), @From, @To, @Reason, 'Pending');
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Emp", EmployeeId);
                HrmsDatabase.AddParameter(command, "@Type", body.RequestType.Trim());
                HrmsDatabase.AddParameter(command, "@From", from.ToDateTime(TimeOnly.MinValue));
                HrmsDatabase.AddParameter(command, "@To", (object?)to?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
                HrmsDatabase.AddParameter(command, "@Reason", reason);
            });

        if (requestId > 0)
            await ApprovalWorkflowEngine.StartAsync(_db, requestId, body.RequestType.Trim(), EmployeeId);

        return Ok(new { message = $"تم إرسال طلب {body.RequestType.Trim()} وهو قيد المراجعة.", requestId });
    }

    /// <summary>الحقول القابلة لطلب تعديلها + قيمتها الحالية (لبناء نموذج «تعديل بياناتي»).</summary>
    [HttpGet("data-change/fields")]
    public async Task<IActionResult> DataChangeFields()
    {
        if (RequireEmployee() is { } bad) return bad;
        var fields = await DataChangeRequestStore.ListEditableAsync(_db, EmployeeId);
        return Ok(fields.Select(f => new { key = f.Key, label = f.Label, currentValue = f.OldValue }));
    }

    public sealed record DataChangeItem(string Key, string? NewValue);
    public sealed record DataChangeBody(List<DataChangeItem> Fields, string? Reason);

    /// <summary>تقديم طلب تعديل بيانات (لا يُطبَّق إلا بعد اعتماد لجنة الموافقة).</summary>
    [HttpPost("data-change")]
    public async Task<IActionResult> SubmitDataChange([FromBody] DataChangeBody body)
    {
        if (RequireEmployee() is { } bad) return bad;
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
INSERT INTO SelfServiceRequests (EmployeeId, RequestType, CreatedAt, Reason, Status)
VALUES (@Emp, @Type, SYSUTCDATETIME(), @Reason, 'Pending');
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

        await ApprovalWorkflowEngine.StartAsync(_db, requestId, DataChangeRequestStore.RequestTypeLabel, EmployeeId);
        return Ok(new { message = $"تم إرسال طلب تعديل البيانات ({savedCount} حقل) وهو قيد المراجعة.", requestId });
    }
}
