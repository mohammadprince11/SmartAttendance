using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.ShiftRules;

/// <summary>
/// منشئ قواعد المناوبات (/ShiftRules) — المرحلة 4 من مودل الحضور بنمط كيان:
/// قائمة القواعد بجملة «في حالة … ← …» + سلايد بناء (نطاق/شرط/أثر).
/// راجع قسمي 10 و15 بدراسة الحضور.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public List<ShiftRuleStore.ShiftRule> Rules { get; set; } = new();
    public List<ShiftTypeStore.ShiftType> Shifts { get; set; } = new();

    public async Task OnGetAsync()
    {
        Rules = await ShiftRuleStore.ListAsync(_dbContext);
        Shifts = (await ShiftTypeStore.ListAsync(_dbContext)).Where(s => s.IsActive).ToList();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var form = Request.Form;
        var rule = new ShiftRuleStore.ShiftRule
        {
            Id = int.TryParse(form["Id"], out var id) ? id : 0,
            Name = form["Name"].ToString().Trim(),
            ShiftTypeIds = string.Join(",", form["ShiftTypeIds"].Where(v => !string.IsNullOrWhiteSpace(v))),
            ApplyOn = form["ApplyOn"].ToString() is { Length: > 0 } applyOn ? applyOn : "Work",
            WeekDays = string.Join(",", form["WeekDays"].Where(v => !string.IsNullOrWhiteSpace(v))),
            ConditionField = form["ConditionField"].ToString() is { Length: > 0 } field ? field : "CheckIn",
            Comparison = form["Comparison"].ToString() is { Length: > 0 } cmp ? cmp : "After",
            ValueKind = form["ValueKind"].ToString() is { Length: > 0 } kind ? kind : "Time",
            ValueTime = string.IsNullOrWhiteSpace(form["ValueTime"]) ? null : form["ValueTime"].ToString(),
            OffsetMinutes = int.TryParse(form["OffsetMinutes"], out var offset) ? offset : 0,
            ValueHours = decimal.TryParse(form["ValueHours"], out var hours) ? hours : 0,
            ActionType = form["ActionType"].ToString() is { Length: > 0 } action ? action : "Violation",
            ActionText = form["ActionText"].ToString().Trim(),
            IsAutomatic = form["IsAutomatic"] == "true",
            IsActive = form["IsActive"] == "true"
        };

        if (string.IsNullOrWhiteSpace(rule.Name) || string.IsNullOrWhiteSpace(rule.ActionText))
        {
            TempData["SuccessMessage"] = "اسم القاعدة ونص الإجراء مطلوبان.";
            return RedirectToPage();
        }

        await ShiftRuleStore.SaveAsync(_dbContext, rule);
        TempData["SuccessMessage"] = rule.Id > 0 ? "تم تحديث القاعدة." : "تمت إضافة القاعدة.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await ShiftRuleStore.DeleteAsync(_dbContext, id);
        TempData["SuccessMessage"] = "تم حذف القاعدة.";
        return RedirectToPage();
    }
}
