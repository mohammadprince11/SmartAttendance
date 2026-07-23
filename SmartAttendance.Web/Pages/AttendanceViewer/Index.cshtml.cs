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

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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
        public Dictionary<int, DayAttendanceStore.DayRow> Days { get; set; } = new(); // اليوم ← اليومية
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
    public int DaysInMonth { get; set; }
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
        DaysInMonth = DateTime.DaysInMonth(year, month);

        await LoadLookupsAsync();

        var all = await DayAttendanceStore.ListAsync(_dbContext, year, month, Search);

        // فلتر سمات الموظف: نجلب معرّفات الموظفين المطابقين ونقصر المصفوفة عليهم
        if (HasFilter)
        {
            var q = _dbContext.Employees.AsNoTracking().Where(e => e.IsActive);
            if (FDept != null) q = q.Where(e => e.DepartmentId == FDept);
            if (FBranch != null) q = q.Where(e => e.BranchId == FBranch);
            if (FPosition != null) q = q.Where(e => e.PositionId == FPosition);
            if (!string.IsNullOrWhiteSpace(FContract)) q = q.Where(e => e.ContractType == FContract);
            if (!string.IsNullOrWhiteSpace(FNationality)) q = q.Where(e => e.Nationality == FNationality);
            var matchIds = new HashSet<int>(await q.Select(e => e.Id).ToListAsync());
            all = all.Where(r => matchIds.Contains(r.EmployeeId)).ToList();
        }

        var employees = all
            .GroupBy(r => (r.EmployeeId, r.EmployeeNo, r.EmployeeName))
            .Select(g => new EmployeeRow
            {
                EmployeeId = g.Key.EmployeeId,
                EmployeeNo = g.Key.EmployeeNo,
                EmployeeName = g.Key.EmployeeName,
                Days = g.ToDictionary(r => r.WorkDate.Day),
                PresentDays = g.Count(r => r.Status is "Present" or "Late"),
                WorkDays = g.Count(r => r.DayKind == "Work")
            })
            .OrderBy(e => e.EmployeeNo)
            .ToList();

        TotalRows = employees.Count;
        TotalPages = TotalRows == 0 ? 1 : (int)Math.Ceiling(TotalRows / (double)PageSize);
        if (PageNumber < 1) PageNumber = 1;
        if (PageNumber > TotalPages) PageNumber = TotalPages;
        Rows = employees.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();

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

    private async Task LoadLookupsAsync()
    {
        Departments = await _dbContext.Departments.AsNoTracking()
            .OrderBy(d => d.Name).Select(d => new Lookup(d.Id.ToString(), d.Name)).ToListAsync();
        Branches = await _dbContext.Branches.AsNoTracking()
            .OrderBy(b => b.Name).Select(b => new Lookup(b.Id.ToString(), b.Name)).ToListAsync();
        Positions = await _dbContext.HrJobPositions.AsNoTracking()
            .OrderBy(p => p.ArabicName).Select(p => new Lookup(p.Id.ToString(), p.ArabicName)).ToListAsync();
        ContractTypes = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.ContractType != null && e.ContractType != "")
            .Select(e => e.ContractType!).Distinct().OrderBy(x => x).ToListAsync();
        Nationalities = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.Nationality != null && e.Nationality != "")
            .Select(e => e.Nationality!).Distinct().OrderBy(x => x).ToListAsync();
        AllEmployees = await _dbContext.Employees.AsNoTracking().Where(e => e.IsActive)
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
            var ok = await DayAttendanceStore.UpdateDayAsync(_dbContext, empId, date, checkIn, checkOut);
            TempData["SuccessMessage"] = ok
                ? $"تم تعديل حضور {date:yyyy-MM-dd}."
                : "تعذّر التعديل — لا يومية محللة لهذا اليوم (شغّل «تحديث الحضور» أولاً).";
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
            if (from == default) from = new DateOnly(year, month, 1);
            if (to == default) to = from.AddMonths(1).AddDays(-1);
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
