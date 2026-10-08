using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;
using SmartAttendance.Web.Infrastructure.Notifications;
using SmartAttendance.Web.Infrastructure.Security;
using SmartAttendance.Web.Infrastructure.Hrms;
using Microsoft.EntityFrameworkCore;

namespace SmartAttendance.Web.Pages.HrSettings;

[Authorize(Roles = RoleRouteCatalog.Admin)]
public class NotificationCenterModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;
    private readonly IEmailSender? _email;

    public NotificationCenterModel(ApplicationDbContext db, ICompanyScopeProvider companyScope, IEmailSender? email = null)
    {
        _db = db;
        _companyScope = companyScope;
        _email = email;
    }

    public List<NotificationRuleRow> Rules { get; private set; } = new();
    public Dictionary<int, NotificationRuleSettings> Settings { get; private set; } = [];
    public sealed record Choice(int Id, string Name);
    public List<Choice> Companies { get; private set; } = [];
    public List<Choice> Employees { get; private set; } = [];
    public int CompanyId { get; private set; }
    public bool EmailAvailable { get; private set; }
    private async Task<bool> EmailReadyAsync() => _email?.IsEnabled == true &&
        await HrmsDatabase.ScalarAsync<int>(_db, "SELECT CASE WHEN OBJECT_ID('ZynoraNotificationMailOutbox', 'U') IS NOT NULL THEN 1 ELSE 0 END;", _ => { }) == 1;
    public static string DisplayName(string name) => NotificationRuleSettings.ReferenceName(name);

    public async Task<IActionResult> OnGetAsync(int? companyId)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var query = _db.Companies.AsNoTracking().Where(c => c.IsActive && !c.IsDeleted);
        if (!scope.IsUnrestricted) query = query.Where(c => scope.AllowedCompanyIds.Contains(c.Id));
        Companies = await query.OrderBy(c => c.Id).Select(c => new Choice(c.Id, c.Name)).ToListAsync(HttpContext.RequestAborted);
        CompanyId = companyId ?? Companies.FirstOrDefault()?.Id ?? 0;
        if (CompanyId <= 0 || Companies.All(c => c.Id != CompanyId)) return Forbid();
        EmailAvailable = await EmailReadyAsync();
        Rules = await HrSettingsStore.LoadNotificationRulesAsync(_db);
        Settings = await NotificationRuleSettings.LoadCompanyAsync(_db, CompanyId);
        foreach (var rule in Rules)
        {
            var settings = Settings.GetValueOrDefault(rule.Id) ?? NotificationRuleSettings.FromLegacy(rule);
            Settings[rule.Id] = settings;
            rule.IsEnabled = settings.IsEnabled; rule.Audience = settings.Audience;
            rule.DaysBefore = settings.DaysBefore; rule.SupervisorName = settings.SupervisorName;
        }
        var selectedIds = Settings.Values.SelectMany(s => s.GroupMembers).Distinct().ToArray();
        Employees = await _db.Employees.AsNoTracking().Where(e => e.CompanyId == CompanyId && e.IsActive && !e.IsDeleted && selectedIds.Contains(e.Id))
            .OrderBy(e => e.FullName).Select(e => new Choice(e.Id, e.FullName)).ToListAsync(HttpContext.RequestAborted);
        return Page();
    }

    private async Task<bool> AllowsCompanyAsync(int companyId)
    {
        if (companyId <= 0) return false;
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        return scope.Allows(companyId) && await _db.Companies.AnyAsync(c => c.Id == companyId && c.IsActive && !c.IsDeleted, HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnGetSearchEmployeesAsync(int companyId, string? search)
    {
        if (!await AllowsCompanyAsync(companyId)) return Forbid();
        var term = search?.Trim() ?? "";
        if (term.Length is < 2 or > 100) return new JsonResult(Array.Empty<Choice>());
        var employees = await _db.Employees.AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.IsActive && !e.IsDeleted && (e.FullName.Contains(term) || e.EmployeeNo.Contains(term)))
            .OrderBy(e => e.FullName).Take(30).Select(e => new Choice(e.Id, e.FullName)).ToListAsync(HttpContext.RequestAborted);
        return new JsonResult(employees);
    }

    private async Task<IActionResult> SaveCompanyAsync(int companyId, NotificationRuleRow rule,
        Func<NotificationRuleSettings, NotificationRuleSettings> update, bool toggle)
    {
        IActionResult result = StatusCode(409, new { message = "يوجد تشغيل آخر الآن، حاول مجدداً." });
        await SqlDistributedLock.TryRunAsync(_db, "ZYNORA.NotificationRuleGenerator", async () =>
        {
            var current = (await NotificationRuleSettings.LoadCompanyAsync(_db, companyId)).GetValueOrDefault(rule.Id) ?? NotificationRuleSettings.FromLegacy(rule);
            var updated = update(current);
            if (updated.IsEnabled && !NotificationRuleCatalog.AllowsSettings(rule.Name, updated))
            { result = BadRequest(new { message = "اختر مستلمين متاحين لهذه القاعدة وراجع عدد أيام التذكير." }); return; }
            if (updated.IsEnabled && updated.Email && !await EmailReadyAsync())
            { result = BadRequest(new { message = "قناة البريد أو هجرتها غير جاهزة؛ أوقف البريد حتى تكتمل التهيئة." }); return; }
            if (updated.IsEnabled && !NotificationRuleSettings.Valid(updated))
            { result = BadRequest(new { message = "صحّح المستلمين والقنوات والقالب أولاً." }); return; }
            await NotificationRuleSettings.SaveAsync(_db, companyId, rule.Id, updated);
            result = !Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase)
                ? RedirectToPage(new { companyId })
                : toggle ? new JsonResult(new { isEnabled = updated.IsEnabled }) : new JsonResult(new { saved = true });
        }, HttpContext.RequestAborted);
        return result;
    }

    public async Task<IActionResult> OnPostToggleRuleAsync(int id, bool? isEnabled = null, int companyId = 0)
    {
        var json = Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
        if (id <= 0 || companyId <= 0 || !ModelState.IsValid || !isEnabled.HasValue) return BadRequest();
        if (!await AllowsCompanyAsync(companyId)) return Forbid();
        Rules = await HrSettingsStore.LoadNotificationRulesAsync(_db);
        var rule = Rules.FirstOrDefault(x => x.Id == id);
        if (json && rule == null) return NotFound();
        if (rule != null)
        {
            var desired = isEnabled ?? !rule.IsEnabled;
            if (desired && NotificationRuleGenerator.MapRuleName(DisplayName(rule.Name)) is null)
            {
                if (json) return BadRequest(new { message = "هذه القاعدة غير مرتبطة بمصدر أحداث بعد." });
                TempData["ErrorMessage"] = "هذه القاعدة غير مرتبطة بمصدر أحداث بعد.";
                return RedirectToPage();
            }
            var kind = NotificationRuleGenerator.MapRuleName(DisplayName(rule.Name));
            return await SaveCompanyAsync(companyId, rule, current => current with
            {
                IsEnabled = desired,
                DaysBefore = NotificationRuleCatalog.HasDays(rule.Name) ? current.DaysBefore : 0,
                EnabledSinceUtc = desired && !current.IsEnabled ? DateTime.UtcNow : current.EnabledSinceUtc
            }, true);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateRuleAsync(int id, string audience, int daysBefore, string selectedItems, string? supervisorName,
        int companyId = 0, bool inApp = true, bool email = false, string? titleTemplate = "", string? bodyTemplate = "", int[]? groupMembers = null,
        string welcomeBasis = "CreatedAt")
    {
        var json = Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
        if (!ModelState.IsValid || id <= 0 || companyId <= 0 || daysBefore is < 0 or > 366
            || !NotificationRoutingPolicy.ValidAudience(audience) || string.IsNullOrWhiteSpace(selectedItems)) return BadRequest();
        if (!await AllowsCompanyAsync(companyId)) return Forbid();
        var proposed = new NotificationRuleSettings { Audience = audience, DaysBefore = daysBefore,
            SupervisorName = supervisorName?.Trim() ?? "", InApp = inApp, Email = email,
            TitleTemplate = titleTemplate ?? "", BodyTemplate = bodyTemplate ?? "", GroupMembers = groupMembers ?? [], WelcomeBasis = welcomeBasis };
        if (!NotificationRuleSettings.Valid(proposed)) return BadRequest(new { message = "تحقق من المستلمين والقنوات ومتغيرات القالب." });
        if (email && !await EmailReadyAsync()) return BadRequest(new { message = "قناة SMTP أو هجرتها غير جاهزة؛ لم يحفظ تفعيل البريد." });
        if (groupMembers is { Length: > 0 })
        {
            var ids = await _db.Employees.Where(e => e.CompanyId == companyId && e.IsActive && !e.IsDeleted && groupMembers.Contains(e.Id))
                .Select(e => e.Id).ToListAsync(HttpContext.RequestAborted);
            if (ids.Count != groupMembers.Length) return BadRequest(new { message = "المجموعة يجب أن تكون ضمن الشركة المحددة." });
        }
        Rules = await HrSettingsStore.LoadNotificationRulesAsync(_db);
        var rule = Rules.FirstOrDefault(x => x.Id == id);
        if (rule is null) return NotFound();
        if (NotificationRuleGenerator.MapRuleName(DisplayName(rule.Name)) is not null)
        {
            if (!NotificationRuleCatalog.AllowsSettings(rule.Name, proposed))
                return BadRequest(new { message = "المستلمون أو أيام التذكير غير متاحين لهذه القاعدة." });
            if (!NotificationRoutingPolicy.ValidEmployeeItems(selectedItems)) return BadRequest();
            if (!string.IsNullOrWhiteSpace(supervisorName) && NotificationRoutingPolicy.IncludesSupervisors(audience))
            {
                var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
                var matches = await HrmsDatabase.QueryAsync(_db, $"""
SELECT DISTINCT su.Id FROM SystemUsers su
JOIN Companies c ON c.TenantId = su.TenantId AND c.Id = @CompanyId AND c.IsActive = 1 AND c.IsDeleted = 0
LEFT JOIN Employees e ON e.Id = su.EmployeeId AND e.CompanyId = c.Id AND e.IsActive = 1 AND e.IsDeleted = 0
WHERE su.IsActive = 1 AND su.IsDeleted = 0 AND su.Role IN (1, 2)
  AND su.UserName = @Username AND (su.EmployeeId IS NULL OR e.Id IS NOT NULL)
  AND {scope.ToSqlPredicate("c.Id")};
""", command => { HrmsDatabase.AddParameter(command, "@Username", supervisorName.Trim()); HrmsDatabase.AddParameter(command, "@CompanyId", companyId); },
                    reader => HrmsDatabase.GetInt(reader, "Id"));
                if (matches.Count != 1) return BadRequest(new { message = "اختر اسم مستخدم مشرف فعّال ضمن نطاقك، وليس الاسم الشخصي." });
            }
        }
        return await SaveCompanyAsync(companyId, rule, current => proposed with { IsEnabled = current.IsEnabled, EnabledSinceUtc = current.EnabledSinceUtc }, false);
    }

}
