using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.PeriodRules;

/// <summary>
/// القواعد الفترية (/PeriodRules) — نمط كيان قسم 36.هـ: باني قواعد شهرية/أسبوعية
/// بشرائح تصاعدية على مقياس مُجمَّع، ومُقيِّم يعرض العقوبة المطابقة لكل موظف بالفترة.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public string EvalPeriodType { get; set; } = "Month";
    [BindProperty(SupportsGet = true)] public int? EvalYear { get; set; }
    [BindProperty(SupportsGet = true)] public int? EvalPeriod { get; set; }
    [BindProperty(SupportsGet = true)] public bool DidEvaluate { get; set; }

    public List<PeriodRuleStore.PeriodRule> Rules { get; set; } = new();
    public List<PeriodRuleStore.Match> Matches { get; set; } = new();
    public int WeeksInYear { get; set; }

    public IReadOnlyList<(string Key, string Label)> PeriodTypes => PeriodRuleStore.PeriodTypes;
    public IReadOnlyList<(string Key, string Label, bool IsHours)> MetricList => PeriodRuleStore.Metrics;
    public IReadOnlyList<(string Key, string Label)> ActionTypeList => PeriodRuleStore.ActionTypes;

    public (int Year, int Period) EvalResolved
    {
        get
        {
            var (curIsoYear, curWeek) = WeekAttendanceStore.Current();
            var today = DateTime.Today;
            if (EvalPeriodType == "Week")
            {
                var year = EvalYear is > 1999 and < 2100 ? EvalYear.Value : curIsoYear;
                var maxW = WeekAttendanceStore.WeeksInYear(year);
                var week = EvalPeriod is int w && w >= 1 && w <= maxW ? w : curWeek;
                return (year, week);
            }
            var yy = EvalYear is > 1999 and < 2100 ? EvalYear.Value : today.Year;
            var mm = EvalPeriod is int m && m is >= 1 and <= 12 ? m : today.Month;
            return (yy, mm);
        }
    }

    public async Task OnGetAsync()
    {
        Rules = await PeriodRuleStore.ListRulesAsync(_db);

        var (year, period) = EvalResolved;
        EvalYear = year;
        EvalPeriod = period;
        WeeksInYear = WeekAttendanceStore.WeeksInYear(year);

        if (DidEvaluate)
        {
            Matches = await PeriodRuleStore.EvaluateAsync(_db, EvalPeriodType, year, period);
        }
    }

    public async Task<IActionResult> OnPostSaveRuleAsync()
    {
        var f = Request.Form;
        var rule = new PeriodRuleStore.PeriodRule
        {
            Id = int.TryParse(f["Id"], out var id) ? id : 0,
            Name = f["Name"].ToString().Trim(),
            PeriodType = f["PeriodType"].ToString() == "Week" ? "Week" : "Month",
            Metric = f["Metric"].ToString() is { Length: > 0 } m ? m : "LateHours",
            IsActive = f["IsActive"] == "true" || f["IsActive"] == "on"
        };

        var froms = f["SliceFrom"];
        var tos = f["SliceTo"];
        var atypes = f["SliceActionType"];
        var atexts = f["SliceActionText"];
        var avalues = f["SliceActionValue"];
        for (var i = 0; i < froms.Count; i++)
        {
            var fromStr = froms[i];
            if (string.IsNullOrWhiteSpace(fromStr)) continue;
            if (!decimal.TryParse(fromStr, out var from)) continue;
            decimal? to = i < tos.Count && decimal.TryParse(tos[i], out var t) ? t : null;
            rule.Slices.Add(new PeriodRuleStore.Slice
            {
                SliceFrom = from,
                SliceTo = to,
                ActionType = i < atypes.Count && !string.IsNullOrWhiteSpace(atypes[i]) ? atypes[i]! : "Violation",
                ActionText = i < atexts.Count ? (atexts[i] ?? string.Empty).Trim() : string.Empty,
                ActionValue = i < avalues.Count && decimal.TryParse(avalues[i], out var av) ? av : 0
            });
        }

        var (ok, message) = await PeriodRuleStore.SaveRuleAsync(_db, rule);
        TempData["PrMessage"] = message;
        TempData["PrOk"] = ok;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteRuleAsync(int id)
    {
        await PeriodRuleStore.DeleteRuleAsync(_db, id);
        TempData["PrMessage"] = "حُذفت القاعدة.";
        TempData["PrOk"] = true;
        return RedirectToPage();
    }
}
