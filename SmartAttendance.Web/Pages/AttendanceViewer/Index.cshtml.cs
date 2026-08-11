using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.AttendanceViewer;

/// <summary>
/// مستعرض الحضور (/AttendanceViewer) — المرحلة 5 من مودل الحضور بنمط كيان:
/// مصفوفة موظف × أيام الشهر بمفتاح حالات ملون + عمود المجموع (حاضر/أيام عمل).
/// يقرأ من يوميات DayAttendances المولّدة بـ«تحديث الحضور».
/// راجع قسمي 3.4 و13 بدراسة الحضور.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly SmartAttendance.Web.Infrastructure.Notifications.IWebPushSender _push;
    private readonly SmartAttendance.Web.Infrastructure.Security.ICompanyScopeProvider _companyScope;

    public IndexModel(ApplicationDbContext dbContext,
        SmartAttendance.Web.Infrastructure.Notifications.IWebPushSender push,
        SmartAttendance.Web.Infrastructure.Security.ICompanyScopeProvider companyScope)
    {
        _dbContext = dbContext;
        _push = push;
        _companyScope = companyScope;
    }

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public string? Month { get; set; }          // "yyyy-MM"

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    // فلتر بحث متقدم (نمط كيان) — سمات الموظف
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public int? FDept { get; set; }
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public int? FBranch { get; set; }
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public int? FPosition { get; set; }
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public string? FContract { get; set; }
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public string? FNationality { get; set; }

    public const int PageSize = 20;

    public record Lookup(string Value, string Label);
    public List<Lookup> Departments { get; set; } = new();
    public List<Lookup> Branches { get; set; } = new();
    public List<Lookup> Positions { get; set; } = new();
    public List<string> ContractTypes { get; set; } = new();
    public List<string> Nationalities { get; set; } = new();
    public record EmpLite(int Id, string No, string Name);
    public List<EmpLite> AllEmployees { get; set; } = new();
    public bool HasFilter => FDept != null || FBranch != null || FPosition != null
        || !string.IsNullOrWhiteSpace(FContract) || !string.IsNullOrWhiteSpace(FNationality);

    public sealed class EmployeeRow
    {
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string? Position { get; set; }
        /// <summary>
        /// **التاريخ** ← اليومية. كان المفتاح رقم اليوم (1..31): يعمل مع فترة الغلق
        /// القياسية بالمصادفة (لأن <c>to &lt; from</c> فلا يتقاطع الطرفان)، لكنه
        /// اعتماد هشّ — أي فترة تتجاوز شهراً تكرّر الرقم فتدهس خليّة خليّةً.
        /// التاريخ لا يلتبس.
        /// </summary>
        public Dictionary<DateOnly, DayAttendanceStore.DayRow> Days { get; set; } = new();
        public int PresentDays { get; set; }
        public int WorkDays { get; set; }

        /// <summary>أول حرفين من الاسم — لشارة الموظف الملوّنة (نمط كيان).</summary>
        public string Initials
        {
            get
            {
                var parts = EmployeeName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "؟";
                return parts.Length == 1 ? parts[0][..1] : $"{parts[0][..1]}{parts[1][..1]}";
            }
        }

        /// <summary>لون شارة الموظف مشتق من المعرّف (ثابت لكل موظف).</summary>
        public string BadgeColor
        {
            get
            {
                var palette = ShiftTypeStore.Colors;
                return palette[EmployeeId % palette.Length];
            }
        }
    }

    public List<EmployeeRow> Rows { get; set; } = new();
    /// <summary>عدد أيام الفترة المعروضة (أعمدة الشبكة) — من فترة الغلق لا من الشهر.</summary>
    public int DaysInMonth { get; set; }

    /// <summary>فترة غلق الحضور المعروضة — مصدر أعمدة الشبكة ومدى الاستعلام.</summary>
    public AttendancePeriodPolicy.Period CutoffPeriod { get; set; }

    /// <summary>اسم سياسة الغلق النشطة (فارغ ⟹ شهر تقويمي كامل).</summary>
    public string? CutoffPolicyName { get; set; }
    public int TotalRows { get; set; }
    public int TotalPages { get; set; }
    public (int Year, int Month) MonthPair => Period;

    public (int Year, int Month) Period
    {
        get
        {
            if (DateTime.TryParse($"{Month}-01", out var parsed)) return (parsed.Year, parsed.Month);
            var today = DateTime.Today;
            return (today.Year, today.Month);
        }
    }

    public async Task OnGetAsync()
    {
        var (year, month) = Period;
        Month ??= $"{year:0000}-{month:00}";

        // العرض يتبع **فترة غلق الحضور** لا الشهر التقويمي — نفس الحدّ الذي يقرأه
        // المسير. بلا سياسة نشطة تعود الفترة للشهر كاملاً فلا يتغيّر شيء.
        (CutoffPeriod, CutoffPolicyName) =
            await AttendancePeriodPolicy.ResolveFromPolicyAsync(_dbContext, year, month);
        DaysInMonth = CutoffPeriod.DayCount;

        var scope = await _companyScope.GetAsync();
        await LoadLookupsAsync(scope);

        // الموجة 4 (P1-2): صفحة الموظفين تُحسم بالـSQL أولاً، ثم تُقرأ يوميات
        // موظفي الصفحة وحدهم — ‎20 × 30 ≈ 600 صفّ بدل تحميل المدى كاملاً
        // وتجميعه بالذاكرة. فلاتر السمات صارت شروطاً بنفس الاستعلام لا مصفوفة
        // معرّفات تُطبَّق بعد التحميل.
        if (PageNumber < 1) PageNumber = 1;

        var (pageIds, totalEmployees) = await DayAttendanceStore.PageViewerEmployeesAsync(
            _dbContext, scope, CutoffPeriod.From, CutoffPeriod.To, Search,
            FDept, FBranch, FPosition, FContract, FNationality,
            PageNumber, PageSize);

        TotalRows = totalEmployees;
        TotalPages = TotalRows == 0 ? 1 : (int)Math.Ceiling(TotalRows / (double)PageSize);
        if (PageNumber > TotalPages) PageNumber = TotalPages;

        var pageDays = await DayAttendanceStore.ListRangeAsync(
            _dbContext, scope, CutoffPeriod.From, CutoffPeriod.To, search: null,
            employeeIds: pageIds);

        Rows = pageDays
            .GroupBy(r => (r.EmployeeId, r.EmployeeNo, r.EmployeeName))
            .Select(g => new EmployeeRow
            {
                EmployeeId = g.Key.EmployeeId,
                EmployeeNo = g.Key.EmployeeNo,
                EmployeeName = g.Key.EmployeeName,
                Days = g.ToDictionary(r => r.WorkDate),
                PresentDays = g.Count(r => r.Status is "Present" or "Late"),
                WorkDays = g.Count(r => r.DayKind == "Work")
            })
            .OrderBy(e => e.EmployeeNo)
            .ToList();

        // المناصب للصفوف المعروضة فقط (بطاقة الموظف نمط كيان)
        var ids = Rows.Select(r => r.EmployeeId).ToList();
        var positions = await _dbContext.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, Pos = e.PositionId != null
                ? _dbContext.HrJobPositions.Where(p => p.Id == e.PositionId).Select(p => p.ArabicName).FirstOrDefault()
                : e.Position })
            .ToDictionaryAsync(x => x.Id, x => x.Pos);
        foreach (var row in Rows)
            if (positions.TryGetValue(row.EmployeeId, out var pos)) row.Position = pos;
    }

    private async Task LoadLookupsAsync(SmartAttendance.Web.Infrastructure.Security.CompanyScope scope)
    {
        // 🛡️ عزل الشركات: قوائم الفلاتر تُقصَر على شركات النطاق حتى لا تُسرَّب أسماء
        // فروع/أقسام/وظائف/موظفي شركاتٍ أخرى في المنسدلات. غير المقيَّد (allowed=null)
        // يرى الكل؛ والنطاق المقيَّد بمصفوفة فارغة (DeniedAll) يُنتج قوائم فارغة تلقائياً
        // لأنّ Contains على مصفوفةٍ فارغة يعيد false — فلا حاجة لفرعٍ خاصّ بالحرمان الكليّ.
        var allowed = scope.IsUnrestricted ? null : scope.AllowedCompanyIds.ToArray();

        var deptQ = _dbContext.Departments.AsNoTracking();
        Departments = await (allowed == null ? deptQ : deptQ.Where(d => allowed.Contains(d.CompanyId)))
            .OrderBy(d => d.Name).Select(d => new Lookup(d.Id.ToString(), d.Name)).ToListAsync();

        var brQ = _dbContext.Branches.AsNoTracking();
        Branches = await (allowed == null ? brQ : brQ.Where(b => allowed.Contains(b.CompanyId)))
            .OrderBy(b => b.Name).Select(b => new Lookup(b.Id.ToString(), b.Name)).ToListAsync();

        var posQ = _dbContext.HrJobPositions.AsNoTracking();
        Positions = await (allowed == null ? posQ : posQ.Where(p => allowed.Contains(p.CompanyId)))
            .OrderBy(p => p.ArabicName).Select(p => new Lookup(p.Id.ToString(), p.ArabicName)).ToListAsync();

        var empQ = _dbContext.Employees.AsNoTracking();
        var empScoped = allowed == null ? empQ
            : empQ.Where(e => e.CompanyId != null && allowed.Contains(e.CompanyId.Value));
        ContractTypes = await empScoped
            .Where(e => e.ContractType != null && e.ContractType != "")
            .Select(e => e.ContractType!).Distinct().OrderBy(x => x).ToListAsync();
        Nationalities = await empScoped
            .Where(e => e.Nationality != null && e.Nationality != "")
            .Select(e => e.Nationality!).Distinct().OrderBy(x => x).ToListAsync();
        AllEmployees = await empScoped.Where(e => e.IsActive)
            .OrderBy(e => e.EmployeeNo).Select(e => new EmpLite(e.Id, e.EmployeeNo, e.FullName)).ToListAsync();
    }

    /// <summary>تعديل بصمتي يوم من الخلية (تحديث الدخول/الخروج وإعادة الاشتقاق).</summary>
    public async Task<IActionResult> OnPostEditDayAsync()
    {
        var form = Request.Form;
        var empId = int.TryParse(form["EmployeeId"], out var e) ? e : 0;
        DateOnly.TryParse(form["Date"], out var date);
        DateTime? checkIn = TimeOnly.TryParse(form["CheckIn"], out var ci) ? date.ToDateTime(ci) : null;
        DateTime? checkOut = TimeOnly.TryParse(form["CheckOut"], out var co) ? date.ToDateTime(co) : null;

        if (empId <= 0 || date == default)
        {
            TempData["SuccessMessage"] = "بيانات التعديل ناقصة.";
        }
        else
        {
            // empId من المتصفّح — الحارس داخل UpdateDayAsync يرفض موظفاً خارج النطاق.
            var ok = await DayAttendanceStore.UpdateDayAsync(
                _dbContext, await _companyScope.GetAsync(), empId, date, checkIn, checkOut);
            TempData["SuccessMessage"] = ok
                ? $"تم تعديل حضور {date:yyyy-MM-dd}."
                : "تعذّر التعديل — الموظف خارج نطاقك أو لا يومية محللة لهذا اليوم (شغّل «تحديث الحضور» أولاً).";
        }
        return RedirectToPage(RouteValues());
    }

    /// <summary>
    /// إشعار الموظفين: مؤلّف رسالة (فترة + قناة + نص برموز) يُرسَل للموظفين المطابقين
    /// للفلتر الحالي؛ تُملأ رموز كل موظف من حضوره الفعلي وتُخزَّن رسالته بصندوق الصادر.
    /// </summary>
    public async Task<IActionResult> OnPostNotifyAsync()
    {
        var (year, month) = Period;
        var form = Request.Form;
        var type = form["NotifType"].ToString() is { Length: > 0 } t ? t : "Summary";
        var channel = form["Channel"].ToString() is { Length: > 0 } ch ? ch : "System";
        var template = form["Message"].ToString();

        DateOnly from, to;
        var week = 0;
        if (AttendanceNotificationStore.IsWeekly(type))
        {
            var wy = int.TryParse(form["NYear"], out var y2) ? y2 : year;
            var wm = int.TryParse(form["NMonth"], out var m2) ? m2 : month;
            week = int.TryParse(form["NWeek"], out var wk) ? Math.Max(1, wk) : 1;
            (from, to) = AttendanceNotificationStore.WeekRange(wy, wm, week);
        }
        else
        {
            DateOnly.TryParse(form["FromDate"], out from);
            DateOnly.TryParse(form["ToDate"], out to);
            // احتياط الشهر يتبع سياسة فترة الحضور (لا التقويم) للاتساق مع بقية الشاشات.
            if (from == default || to == default)
            {
                var (period, _) = await AttendancePeriodPolicy.ResolveFromPolicyAsync(_dbContext, year, month);
                if (from == default) from = period.From;
                if (to == default) to = period.To;
            }
        }

        // النطاق: حقول المؤلّف المستقلة (تسبق فلتر المستعرض إن حُدّدت)
        var scope = form;
        int? sDept = int.TryParse(scope["NDept"], out var sd) ? sd : null;
        int? sBranch = int.TryParse(scope["NBranch"], out var sb) ? sb : null;
        int? sPos = int.TryParse(scope["NPosition"], out var sp) ? sp : null;
        var sContract = scope["NContract"].ToString();
        var sNat = scope["NNationality"].ToString();

        var q = _dbContext.Employees.AsNoTracking().Where(e => e.IsActive);
        if (sDept != null) q = q.Where(e => e.DepartmentId == sDept);
        if (sBranch != null) q = q.Where(e => e.BranchId == sBranch);
        if (sPos != null) q = q.Where(e => e.PositionId == sPos);
        if (!string.IsNullOrWhiteSpace(sContract)) q = q.Where(e => e.ContractType == sContract);
        if (!string.IsNullOrWhiteSpace(sNat)) q = q.Where(e => e.Nationality == sNat);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var v = Search.Trim();
            q = q.Where(e => e.EmployeeNo.Contains(v) || e.FullName.Contains(v));
        }
        var employeeIds = await q.Select(e => e.Id).ToListAsync();
        if (employeeIds.Count == 0)
        {
            TempData["SuccessMessage"] = "لا موظفون مطابقون للنطاق — عدّله.";
            return RedirectToPage(RouteValues());
        }

        var ccMode = form["CcMode"].ToString() is { Length: > 0 } cm ? cm : "None";
        var ccIds = form["CcEmployeeIds"]
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => int.TryParse(x, out var id) ? id : 0)
            .Where(id => id > 0).Distinct().ToList();

        var (_, recipients, ccCount) = await AttendanceNotificationStore.SendAsync(
            _dbContext, type, from, to, week, channel, template, employeeIds, ccMode, ccIds);

        // دفع فوري (Web-Push) لكل موظف مستهدف — best-effort موازٍ للصادر: التطبيق
        // المثبَّت يومض إشعاراً حالاً بلا انتظار البريد. آمن حين القناة معطّلة (No-Op).
        if (_push.IsEnabled)
        {
            var title = AttendanceNotificationStore.LabelOf(type);
            var body = $"لديك إشعار حضور جديد ({from:MM-dd} → {to:MM-dd}). افتح البوابة للتفاصيل.";
            foreach (var empId in employeeIds)
                await _push.SendToEmployeeAsync(_dbContext, empId,
                    new SmartAttendance.Web.Infrastructure.Notifications.PushPayload(title, body, "/EmployeePortal"));
        }

        var channelLabel = AttendanceNotificationStore.Channels.FirstOrDefault(c => c.Key == channel).Label ?? channel;
        TempData["SuccessMessage"] =
            $"تم تأليف إشعار «{AttendanceNotificationStore.LabelOf(type)}» عبر {channelLabel} لـ{recipients} موظفاً"
            + (ccCount > 0 ? $" (+{ccCount} نسخة)" : "")
            + $" ({from:yyyy-MM-dd} → {to:yyyy-MM-dd}) — وُلّدت الرسائل بصندوق الصادر.";
        return RedirectToPage(RouteValues());
    }

    private object RouteValues() => new
    {
        Month, Search, PageNumber, FDept, FBranch, FPosition, FContract, FNationality
    };
}
