using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.ShiftTypes;

/// <summary>
/// أنواع المناوبات (/ShiftTypes) — المرحلة 1 من إعادة بناء مودل الحضور بنمط كيان:
/// قائمة المناوبات + سلايد بناء بمصفوفة 7 أيام (نوع اليوم + دخول/خروج + ساعات)
/// أو مناوبة مرنة بساعات يومية مطلوبة. راجع قسمي 11 و15 بدراسة الحضور.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public List<ShiftTypeStore.ShiftType> Shifts { get; set; } = new();

    // قوائم منتقيات معايير الاستحقاق (تبويب 4)
    public record Lookup(string Value, string Label);
    public List<Lookup> Departments { get; set; } = new();
    public List<Lookup> Branches { get; set; } = new();
    public List<Lookup> Positions { get; set; } = new();
    public List<Lookup> Employees { get; set; } = new();

    public async Task OnGetAsync()
    {
        Shifts = await ShiftTypeStore.ListAsync(_dbContext);
        await LoadLookupsAsync();
    }

    private async Task LoadLookupsAsync()
    {
        Departments = await _dbContext.Departments.AsNoTracking()
            .OrderBy(d => d.Name).Select(d => new Lookup(d.Id.ToString(), d.Name)).ToListAsync();
        Branches = await _dbContext.Branches.AsNoTracking()
            .OrderBy(b => b.Name).Select(b => new Lookup(b.Id.ToString(), b.Name)).ToListAsync();
        Positions = await _dbContext.HrJobPositions.AsNoTracking()
            .OrderBy(p => p.ArabicName).Select(p => new Lookup(p.Id.ToString(), p.ArabicName)).ToListAsync();
        Employees = await _dbContext.Employees.AsNoTracking().Where(e => e.IsActive)
            .OrderBy(e => e.FullName).Select(e => new Lookup(e.Id.ToString(), e.FullName)).ToListAsync();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var form = Request.Form;

        var shift = new ShiftTypeStore.ShiftType
        {
            Id = int.TryParse(form["Id"], out var id) ? id : 0,
            Name = form["Name"].ToString().Trim(),
            NameEn = string.IsNullOrWhiteSpace(form["NameEn"]) ? null : form["NameEn"].ToString().Trim(),
            ColorHex = form["ColorHex"].ToString() is { Length: > 0 } color ? color : "#12D9E3",
            IsFlexible = form["ShiftMode"] == "flex",
            FlexDailyHours = decimal.TryParse(form["FlexDailyHours"], out var flexHours) ? flexHours : 0,
            MultiPeriod = form["PeriodMode"] == "multi" && form["ShiftMode"] != "flex",
            FillMissingCheckIn = form["FillMissingCheckIn"] == "true",
            FillMissingCheckOut = form["FillMissingCheckOut"] == "true",
            StripSemantics = form["StripSemantics"] == "true",
            ConsiderPermissionsOutsideShift = form["ConsiderPermissionsOutsideShift"] == "true",
            ExcludePermsOutsideStartFromLate = form["ExcludePermsOutsideStartFromLate"] == "true",
            TotalDurationMode = form["TotalDurationMode"].ToString() is { Length: > 0 } tdm ? tdm : "WorkOnly",
            AvailableInRoster = form["AvailableInRoster"] == "true",
            RequestableFromEss = form["RequestableFromEss"] == "true",
            LatenessGraceMinutes = int.TryParse(form["LatenessGraceMinutes"], out var lgm) ? Math.Max(0, lgm) : 0,
            EarlyLeaveGraceMinutes = int.TryParse(form["EarlyLeaveGraceMinutes"], out var elg) ? Math.Max(0, elg) : 0,
            GraceExceededPolicy = form["GraceExceededPolicy"] == "Full" ? "Full" : "Subtract",
            TimeLimitFrom = string.IsNullOrWhiteSpace(form["TimeLimitFrom"]) ? null : form["TimeLimitFrom"].ToString(),
            TimeLimitFromDayBefore = form["TimeLimitFromAnchor"] == "before",
            TimeLimitTo = string.IsNullOrWhiteSpace(form["TimeLimitTo"]) ? null : form["TimeLimitTo"].ToString(),
            TimeLimitToDayAfter = form["TimeLimitToAnchor"] == "after",
            MidShiftTime = string.IsNullOrWhiteSpace(form["MidShiftTime"]) ? null : form["MidShiftTime"].ToString(),
            // تبويب 3: قواعد التعارض مع المغادرات
            ConflictLateReturnEnabled = form["ConflictLateReturnEnabled"] == "true",
            ConflictLateReturnAction = form["ConflictLateReturnAction"].ToString() is { Length: > 0 } clra ? clra : "Deduction",
            ConflictLateReturnValue = decimal.TryParse(form["ConflictLateReturnValue"], out var clrv) ? Math.Max(0, clrv) : 0,
            ConflictEarlyLeaveEnabled = form["ConflictEarlyLeaveEnabled"] == "true",
            ConflictEarlyLeaveAction = form["ConflictEarlyLeaveAction"].ToString() is { Length: > 0 } cela ? cela : "Deduction",
            ConflictEarlyLeaveValue = decimal.TryParse(form["ConflictEarlyLeaveValue"], out var celv) ? Math.Max(0, celv) : 0,
            IsActive = form["IsActive"] == "true"
        };

        if (string.IsNullOrWhiteSpace(shift.Name))
        {
            await LoadLookupsAsync();
            TempData["SuccessMessage"] = "اسم المناوبة مطلوب.";
            return RedirectToPage();
        }

        // تبويب 4: معايير الاستحقاق — مصفوفات متوازية (EligGroup/EligField/EligValue)
        var eligGroups = form["EligGroup"];
        var eligFields = form["EligField"];
        var eligValues = form["EligValue"];
        for (var i = 0; i < eligValues.Count; i++)
        {
            var value = eligValues[i]?.Trim();
            if (string.IsNullOrWhiteSpace(value)) continue;
            shift.Eligibility.Add(new ShiftTypeStore.EligibilityRule
            {
                GroupNo = i < eligGroups.Count && int.TryParse(eligGroups[i], out var g) ? g : 0,
                Field = i < eligFields.Count && !string.IsNullOrWhiteSpace(eligFields[i]) ? eligFields[i]! : "Department",
                Value = value
            });
        }

        // فترات السبليت شفت: period_start_i / period_end_i (i=0..)
        if (shift.MultiPeriod)
        {
            for (var i = 0; i < 12; i++)
            {
                var ps = form[$"period_start_{i}"].ToString();
                var pe = form[$"period_end_{i}"].ToString();
                if (string.IsNullOrWhiteSpace(ps) || string.IsNullOrWhiteSpace(pe)) continue;
                shift.Periods.Add(new ShiftTypeStore.ShiftPeriod { Ordinal = shift.Periods.Count, StartTime = ps, EndTime = pe });
            }
            if (shift.Periods.Count == 0) shift.MultiPeriod = false; // لا فترات ⇒ عد لفترة واحدة
        }

        // مصفوفة الأيام السبعة: day_kind_0..6 + day_start/end_0..6
        for (var dayIndex = 0; dayIndex < 7; dayIndex++)
        {
            var kind = form[$"day_kind_{dayIndex}"].ToString() is { Length: > 0 } k ? k : "Work";
            var start = form[$"day_start_{dayIndex}"].ToString();
            var end = form[$"day_end_{dayIndex}"].ToString();
            var isWork = kind == "Work";

            shift.Days.Add(new ShiftTypeStore.ShiftDay
            {
                DayIndex = dayIndex,
                DayKind = kind,
                StartTime = isWork && !shift.IsFlexible && !string.IsNullOrWhiteSpace(start) ? start : null,
                EndTime = isWork && !shift.IsFlexible && !string.IsNullOrWhiteSpace(end) ? end : null,
                WorkHours = !isWork ? 0
                    : shift.IsFlexible ? shift.FlexDailyHours
                    : ShiftTypeStore.ComputeHours(start, end)
            });
        }

        await ShiftTypeStore.SaveAsync(_dbContext, shift);
        TempData["SuccessMessage"] = shift.Id > 0 ? "تم تحديث المناوبة." : "تمت إضافة المناوبة.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await ShiftTypeStore.DeleteAsync(_dbContext, id);
        TempData["SuccessMessage"] = "تم حذف المناوبة.";
        return RedirectToPage();
    }
}
