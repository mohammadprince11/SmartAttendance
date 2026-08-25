using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;
using SmartAttendance.Web.Infrastructure.CompanyContext;

namespace SmartAttendance.Web.Pages.HrSettings;

/// <summary>
/// مركز قوالب الموافقات (نمط كيان — قسم 18.1): قوالب حسب نوع الطلب، قالب = لجنة
/// مرتّبة + شروط + مشاهدون + مصفوفة إشعارات + تصعيد + قواعد. الترتيب بالسحب = أولوية.
/// </summary>
public class ApprovalTemplatesModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyScopeProvider _companyScope;

    public ApprovalTemplatesModel(ApplicationDbContext dbContext, ICompanyScopeProvider companyScope)
    {
        _dbContext = dbContext;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)]
    public string Type { get; set; } = "LeaveRequest";
    [BindProperty(SupportsGet = true)] public int? CompanyId { get; set; }
    public List<Option> Companies { get; set; } = new();

    public Dictionary<string, int> Counts { get; set; } = new();
    public List<ApprovalTemplateStore.TemplateRow> Templates { get; set; } = new();
    public ApprovalTemplateStore.RequestTypeDef? SelectedType { get; set; }

    public sealed record Option(int Id, string Name);
    public List<Option> Branches { get; set; } = new();
    public List<Option> Departments { get; set; } = new();
    public List<string> WorkTypes { get; set; } = new();
    public List<string> Users { get; set; } = new();
    public List<ApprovalDelegationStore.Row> Delegations { get; set; } = new();
    public string CompanyTimeZoneId { get; set; } = "UTC";

    public async Task OnGetAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        Companies = (await _dbContext.Companies.AsNoTracking().Where(company => !company.IsDeleted && company.IsActive)
            .OrderBy(company => company.Name).Select(company => new Option(company.Id,company.Name)).ToListAsync())
            .Where(company => scope.Allows(company.Id)).ToList();
        CompanyId = CompanySelectionContext.Resolve(HttpContext, CompanyId, Companies.Select(company => company.Id).ToArray());
        if (CompanyId is not > 0 || !scope.Allows(CompanyId.Value)) return;
        SelectedType = ApprovalTemplateStore.RequestTypes.FirstOrDefault(t => t.Key.Equals(Type, StringComparison.OrdinalIgnoreCase))
                       ?? ApprovalTemplateStore.RequestTypes[0];
        Type = SelectedType.Key;

        Counts = await ApprovalTemplateStore.CountsAsync(_dbContext, scope, CompanyId.Value);
        Templates = await ApprovalTemplateStore.ListAsync(_dbContext, CompanyId.Value, Type);
        await LoadLookupsAsync();
        Delegations = await ApprovalDelegationStore.ListAsync(_dbContext, scope, CompanyId.Value);
    }

    private async Task LoadLookupsAsync()
    {
        Branches = await _dbContext.Branches.AsNoTracking()
            .Where(b => !b.IsDeleted && b.IsActive && b.CompanyId == CompanyId)
            .OrderBy(b => b.Name)
            .Select(b => new Option(b.Id, b.Name))
            .ToListAsync();

        Departments = await _dbContext.Departments.AsNoTracking()
            .Where(d => !d.IsDeleted && d.IsActive && d.CompanyId == CompanyId)
            .OrderBy(d => d.Name)
            .Select(d => new Option(d.Id, d.Name))
            .ToListAsync();

        WorkTypes = await HrLookups.ValuesAsync(_dbContext, "worktypes");

        Users = await _dbContext.SystemUsers.AsNoTracking()
            .Where(u => !u.IsDeleted && u.IsActive && u.Employee != null && u.Employee.CompanyId == CompanyId)
            .OrderBy(u => u.UserName)
            .Select(u => u.UserName)
            .ToListAsync();

        CompanyTimeZoneId = await _dbContext.Companies.AsNoTracking()
            .Where(company => company.Id == CompanyId)
            .Select(company => company.TimeZoneId)
            .FirstOrDefaultAsync() ?? "UTC";
    }

    public async Task<IActionResult> OnPostCreateDelegationAsync(
        string delegatorUserName,string delegateUserName,DateTime startsAt,DateTime endsAt)
    {
        var scope=await _companyScope.GetAsync(HttpContext.RequestAborted);
        if(CompanyId is not >0||!scope.Allows(CompanyId.Value)) return Forbid();
        var zoneId=await _dbContext.Companies.AsNoTracking().Where(x=>x.Id==CompanyId)
            .Select(x=>x.TimeZoneId).FirstOrDefaultAsync()??"UTC";
        var result=await ApprovalDelegationStore.CreateAsync(_dbContext,scope,CompanyId.Value,
            delegatorUserName,delegateUserName,CompanyLocalToUtc(startsAt,zoneId),CompanyLocalToUtc(endsAt,zoneId),
            User.Identity?.Name??"HR");
        TempData["SuccessMessage"]=result.Message;
        return RedirectToPage(new{Type,CompanyId});
    }

    public async Task<IActionResult> OnPostRevokeDelegationAsync(int id)
    {
        var scope=await _companyScope.GetAsync(HttpContext.RequestAborted);
        if(CompanyId is not >0||!scope.Allows(CompanyId.Value)) return Forbid();
        var changed=await ApprovalDelegationStore.RevokeAsync(_dbContext,scope,CompanyId.Value,id,User.Identity?.Name??"HR");
        TempData["SuccessMessage"]=changed?"تم إلغاء التفويض.":"التفويض غير موجود أو ملغى مسبقاً.";
        return RedirectToPage(new{Type,CompanyId});
    }

    public string CompanyTime(DateTime utc)
    {
        var value=DateTime.SpecifyKind(utc,DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(value,FindZone(CompanyTimeZoneId)).ToString("yyyy-MM-dd HH:mm");
    }

    private static DateTime CompanyLocalToUtc(DateTime local,string zoneId)
        => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local,DateTimeKind.Unspecified),FindZone(zoneId));

    private static TimeZoneInfo FindZone(string? zoneId)
    {
        if(string.IsNullOrWhiteSpace(zoneId)) return TimeZoneInfo.Utc;
        try{return TimeZoneInfo.FindSystemTimeZoneById(zoneId);}
        catch(TimeZoneNotFoundException){return TimeZoneInfo.Utc;}
        catch(InvalidTimeZoneException){return TimeZoneInfo.Utc;}
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (CompanyId is not > 0 || !scope.Allows(CompanyId.Value)) return Forbid();
        var form = Request.Form;

        var template = new ApprovalTemplateStore.TemplateRow
        {
            Id = int.TryParse(form["Id"], out var id) ? id : 0,
            CompanyId = CompanyId.Value,
            RequestType = Type,
            Name = form["Name"].ToString().Trim(),
            NameEn = NullIfEmpty(form["NameEn"]),
            IsActive = form["IsActive"] == "true",
            HasConditions = form["HasConditions"] == "true",
            CondBranchId = ParseNullableInt(form["CondBranchId"]),
            CondDepartmentId = ParseNullableInt(form["CondDepartmentId"]),
            CondWorkType = NullIfEmpty(form["CondWorkType"]),
            AutoRejectUnknownCommittee = form["AutoReject"] == "true",
            CancelLimitDays = ParseNullableInt(form["CancelLimitDays"]),
            CommentRequiredOnReject = form["CommentReq"] == "true",
            AttachmentRequiredOnRequest = form["AttachReq"] == "true",
            EscalationDays = ParseNullableInt(form["EscalationDays"]),
            EscalationTo = NullIfEmpty(form["EscalationTo"]),
            NotifyJson = BuildNotifyJson(form)
        };

        if (string.IsNullOrWhiteSpace(template.Name))
        {
            TempData["SuccessMessage"] = "اسم القالب مطلوب.";
            return RedirectToPage(new { Type, CompanyId });
        }

        // شروط معطّلة = تُمسح حتى لا تبقى شروط خفية.
        if (!template.HasConditions)
        {
            template.CondBranchId = null;
            template.CondDepartmentId = null;
            template.CondWorkType = null;
        }

        var stepTypes = form["StepType"];
        var stepRoles = form["StepRole"];
        var stepUsers = form["StepUser"];
        var stepStages = form["StepStage"];
        for (var i = 0; i < stepTypes.Count; i++)
        {
            var approverType = stepTypes[i] ?? "DirectManager";
            var role = stepRoles.Count > i ? NullIfEmpty(stepRoles[i]) : null;
            var user = stepUsers.Count > i ? NullIfEmpty(stepUsers[i]) : null;
            template.Steps.Add(new ApprovalTemplateStore.StepRow
            {
                ApproverType = approverType,
                StageOrder = stepStages.Count>i&&int.TryParse(stepStages[i],out var stage)&&stage>0?stage:i+1,
                RoleName = approverType == "Role" ? role : null,
                UserName = approverType == "User" ? user : null,
                DisplayName = approverType switch
                {
                    "Role" => role ?? "دور",
                    "User" => user ?? "مستخدم",
                    _ => "المدير المباشر"
                }
            });
        }

        if (ApprovalTemplateStore.Validate(template) is { } validationError)
        {
            TempData["SuccessMessage"] = validationError;
            return RedirectToPage(new { Type, CompanyId });
        }

        foreach (var watcher in form["Watchers"])
        {
            if (!string.IsNullOrWhiteSpace(watcher))
            {
                template.Watchers.Add(new ApprovalTemplateStore.WatcherRow { UserName = watcher });
            }
        }

        await ApprovalTemplateStore.SaveAsync(_dbContext, scope, template);
        TempData["SuccessMessage"] = template.Id > 0 ? "تم تحديث القالب." : "تم إنشاء القالب.";
        return RedirectToPage(new { Type, CompanyId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (CompanyId is not > 0 || !scope.Allows(CompanyId.Value)) return Forbid();
        await ApprovalTemplateStore.DeleteAsync(_dbContext, scope, CompanyId.Value, id);
        TempData["SuccessMessage"] = "تم حذف القالب.";
        return RedirectToPage(new { Type, CompanyId });
    }

    public async Task<IActionResult> OnPostReorderAsync(string order)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (CompanyId is not > 0 || !scope.Allows(CompanyId.Value)) return Forbid();
        var ids = (order ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var value) ? value : 0)
            .Where(value => value > 0)
            .ToList();

        await ApprovalTemplateStore.ReorderAsync(_dbContext, scope, CompanyId.Value, Type, ids);
        return new JsonResult(new { ok = true });
    }

    /// <summary>محاكاة: أي قالب ينطبق على موظف معيّن (بفرعه/قسمه/نوع دوامه).</summary>
    public async Task<IActionResult> OnGetResolveAsync(string type, int employeeId)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (CompanyId is not > 0 || !scope.Allows(CompanyId.Value)) return Forbid();
        var employee = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId && e.CompanyId == CompanyId)
            .Select(e => new { e.Id, e.FullName, e.CompanyId, e.BranchId, e.DepartmentId, e.WorkType })
            .FirstOrDefaultAsync();

        if (employee == null)
        {
            return new JsonResult(new { found = false, message = "الموظف غير موجود." });
        }

        var template = await ApprovalTemplateStore.ResolveAsync(
            _dbContext, employee.CompanyId ?? CompanyId.Value, type, employee.BranchId, employee.DepartmentId, employee.WorkType);

        if (template == null)
        {
            return new JsonResult(new { found = false, employee = employee.FullName, message = "لا يوجد قالب نشط ينطبق — يسري المسار الافتراضي (المدير المباشر ثم HR)." });
        }

        return new JsonResult(new
        {
            found = true,
            employee = employee.FullName,
            template = template.Name,
            conditional = template.HasConditions,
            chain = template.Steps.OrderBy(s => s.StepOrder).Select(s => s.DisplayName).ToList()
        });
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? ParseNullableInt(string? value)
        => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;

    private static string BuildNotifyJson(IFormCollection form)
    {
        var matrix = new Dictionary<string, List<string>>();
        foreach (var audience in new[] { "Employee", "Committee" })
        {
            var events = new List<string>();
            foreach (var evt in new[] { "Submit", "Approve", "Reject", "Cancel" })
            {
                if (form[$"notify_{audience}_{evt}"] == "true") events.Add(evt);
            }
            if (events.Count > 0) matrix[audience] = events;
        }
        return matrix.Count == 0 ? string.Empty : JsonSerializer.Serialize(matrix);
    }
}
