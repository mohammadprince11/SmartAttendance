using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.ShiftRules;

/// <summary>
/// منشئ قواعد المناوبات (/ShiftRules) — المرحلة 4 من مودل الحضور بنمط كيان:
/// قائمة القواعد بجملة «في حالة … ← …» + سلايد بناء (نطاق/شرط/أثر).
/// راجع قسمي 10 و15 بدراسة الحضور.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyScopeProvider _scopeProvider;

    public IndexModel(ApplicationDbContext dbContext, ICompanyScopeProvider scopeProvider)
    {
        _dbContext = dbContext;
        _scopeProvider = scopeProvider;
    }

    public List<ShiftRuleStore.ShiftRule> Rules { get; set; } = new();
    public List<ShiftTypeStore.ShiftType> Shifts { get; set; } = new();
    public List<PunchSemanticStore.PunchSemantic> Semantics { get; set; } = new();

    /// <summary>كتالوج معايير الاستحقاق لمحرّر الشروط المشترك (zynora-conditions.js).</summary>
    public string CriteriaJson { get; private set; } = "[]";

    public async Task OnGetAsync()
    {
        // العرض محصورٌ بالنطاق (المشترك + شركات المستخدم) — لا الاحتساب.
        var scope = await _scopeProvider.GetAsync();
        var allowedRuleIds = await ConfigTenantScope.AllowedIdsAsync(
            _dbContext, ConfigTenantScope.ShiftRules, scope);

        Rules = (await ShiftRuleStore.ListAsync(_dbContext))
            .Where(rule => scope.IsUnrestricted || allowedRuleIds.Contains(rule.Id))
            .ToList();
        Shifts = (await ShiftTypeStore.ListInScopeAsync(_dbContext, scope)).Where(s => s.IsActive).ToList();
        Semantics = (await PunchSemanticStore.ListAsync(_dbContext)).Where(s => s.IsActive).ToList();
        CriteriaJson = await HrConditionOptions.BuildCatalogJsonAsync(_dbContext);
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
            PunchSemanticId = int.TryParse(form["PunchSemanticId"], out var semanticId) && semanticId > 0 ? semanticId : null,
            ConditionField = form["ConditionField"].ToString() is { Length: > 0 } field ? field : "CheckIn",
            Comparison = form["Comparison"].ToString() is { Length: > 0 } cmp ? cmp : "After",
            ValueKind = form["ValueKind"].ToString() is { Length: > 0 } kind ? kind : "Time",
            ValueTime = string.IsNullOrWhiteSpace(form["ValueTime"]) ? null : form["ValueTime"].ToString(),
            ValueAnchor = form["ValueAnchor"].ToString() is { Length: > 0 } anchor ? anchor : "Same",
            ValueTime2 = string.IsNullOrWhiteSpace(form["ValueTime2"]) ? null : form["ValueTime2"].ToString(),
            ValueAnchor2 = form["ValueAnchor2"].ToString() is { Length: > 0 } anchor2 ? anchor2 : "Same",
            OffsetMinutes = int.TryParse(form["OffsetMinutes"], out var offset) ? offset : 0,
            ValueHours = decimal.TryParse(form["ValueHours"], out var hours) ? hours : 0,
            ValueHours2 = decimal.TryParse(form["ValueHours2"], out var hours2) ? hours2 : 0,
            ActionType = form["ActionType"].ToString() is { Length: > 0 } action ? action : "Violation",
            ActionText = form["ActionText"].ToString().Trim(),
            ActionValue = decimal.TryParse(form["ActionValue"], out var actionValue) ? actionValue : 0,
            AllowEdit = form["AllowEdit"] == "true",
            UseEscalation = form["UseEscalation"] == "true",
            IsAutomatic = form["IsAutomatic"] == "true",
            IsActive = form["IsActive"] == "true",
            ConditionsJson = form["ConditionsJson"].ToString()
        };

        if (string.IsNullOrWhiteSpace(rule.Name) || string.IsNullOrWhiteSpace(rule.ActionText))
        {
            TempData["SuccessMessage"] = "اسم القاعدة ونص الإجراء مطلوبان.";
            return RedirectToPage();
        }

        var scope = await _scopeProvider.GetAsync();
        var isUpdate = rule.Id > 0;

        // معرّفٌ من المتصفّح لا يُوثَق: تعديل قاعدة خارج النطاق = تهيئة شركة أخرى.
        if (isUpdate && !await ConfigTenantScope.IsInScopeAsync(
                _dbContext, ConfigTenantScope.ShiftRules, rule.Id, scope))
        {
            return NotFound();
        }

        // المعرّف من المتجر لا من النموذج: الإدراج يولّده، وبدونه تفشل النسبة بصمت.
        var savedId = await ShiftRuleStore.SaveAsync(_dbContext, rule);

        if (!isUpdate)
        {
            await ConfigTenantScope.AssignCompanyAsync(
                _dbContext, ConfigTenantScope.ShiftRules, savedId, ConfigTenantScope.OwningCompany(scope));
        }

        TempData["SuccessMessage"] = isUpdate ? "تم تحديث القاعدة." : "تمت إضافة القاعدة.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (!await ConfigTenantScope.IsInScopeAsync(
                _dbContext, ConfigTenantScope.ShiftRules, id, await _scopeProvider.GetAsync()))
        {
            return NotFound();
        }

        await ShiftRuleStore.DeleteAsync(_dbContext, id);
        TempData["SuccessMessage"] = "تم حذف القاعدة.";
        return RedirectToPage();
    }
}
