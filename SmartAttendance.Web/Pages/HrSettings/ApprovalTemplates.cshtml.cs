using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.HrSettings;

/// <summary>
/// مركز قوالب الموافقات (نمط كيان — قسم 18.1): قوالب حسب نوع الطلب، قالب = لجنة
/// مرتّبة + شروط + مشاهدون + مصفوفة إشعارات + تصعيد + قواعد. الترتيب بالسحب = أولوية.
/// </summary>
public class ApprovalTemplatesModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public ApprovalTemplatesModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string Type { get; set; } = "LeaveRequest";

    public Dictionary<string, int> Counts { get; set; } = new();
    public List<ApprovalTemplateStore.TemplateRow> Templates { get; set; } = new();
    public ApprovalTemplateStore.RequestTypeDef? SelectedType { get; set; }

    public sealed record Option(int Id, string Name);
    public List<Option> Branches { get; set; } = new();
    public List<Option> Departments { get; set; } = new();
    public List<string> WorkTypes { get; set; } = new();
    public List<string> Users { get; set; } = new();

    public async Task OnGetAsync()
    {
        SelectedType = ApprovalTemplateStore.RequestTypes.FirstOrDefault(t => t.Key.Equals(Type, StringComparison.OrdinalIgnoreCase))
                       ?? ApprovalTemplateStore.RequestTypes[0];
        Type = SelectedType.Key;

        Counts = await ApprovalTemplateStore.CountsAsync(_dbContext);
        Templates = await ApprovalTemplateStore.ListAsync(_dbContext, Type);
        await LoadLookupsAsync();
    }

    private async Task LoadLookupsAsync()
    {
        Branches = await _dbContext.Branches.AsNoTracking()
            .Where(b => !b.IsDeleted && b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new Option(b.Id, b.Name))
            .ToListAsync();

        Departments = await _dbContext.Departments.AsNoTracking()
            .Where(d => !d.IsDeleted && d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => new Option(d.Id, d.Name))
            .ToListAsync();

        WorkTypes = await HrLookups.ValuesAsync(_dbContext, "worktypes");

        Users = await _dbContext.SystemUsers.AsNoTracking()
            .Where(u => !u.IsDeleted && u.IsActive)
            .OrderBy(u => u.UserName)
            .Select(u => u.UserName)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var form = Request.Form;

        var template = new ApprovalTemplateStore.TemplateRow
        {
            Id = int.TryParse(form["Id"], out var id) ? id : 0,
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
            return RedirectToPage(new { Type });
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
        for (var i = 0; i < stepTypes.Count; i++)
        {
            var approverType = stepTypes[i] ?? "DirectManager";
            var role = stepRoles.Count > i ? NullIfEmpty(stepRoles[i]) : null;
            var user = stepUsers.Count > i ? NullIfEmpty(stepUsers[i]) : null;
            template.Steps.Add(new ApprovalTemplateStore.StepRow
            {
                ApproverType = approverType,
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

        if (template.Steps.Count == 0)
        {
            TempData["SuccessMessage"] = "لجنة الموافقة يجب أن تحتوي خطوة واحدة على الأقل.";
            return RedirectToPage(new { Type });
        }

        foreach (var watcher in form["Watchers"])
        {
            if (!string.IsNullOrWhiteSpace(watcher))
            {
                template.Watchers.Add(new ApprovalTemplateStore.WatcherRow { UserName = watcher });
            }
        }

        await ApprovalTemplateStore.SaveAsync(_dbContext, template);
        TempData["SuccessMessage"] = template.Id > 0 ? "تم تحديث القالب." : "تم إنشاء القالب.";
        return RedirectToPage(new { Type });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await ApprovalTemplateStore.DeleteAsync(_dbContext, id);
        TempData["SuccessMessage"] = "تم حذف القالب.";
        return RedirectToPage(new { Type });
    }

    public async Task<IActionResult> OnPostReorderAsync(string order)
    {
        var ids = (order ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var value) ? value : 0)
            .Where(value => value > 0)
            .ToList();

        await ApprovalTemplateStore.ReorderAsync(_dbContext, Type, ids);
        return new JsonResult(new { ok = true });
    }

    /// <summary>محاكاة: أي قالب ينطبق على موظف معيّن (بفرعه/قسمه/نوع دوامه).</summary>
    public async Task<IActionResult> OnGetResolveAsync(string type, int employeeId)
    {
        var employee = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => new { e.Id, e.FullName, e.BranchId, e.DepartmentId, e.WorkType })
            .FirstOrDefaultAsync();

        if (employee == null)
        {
            return new JsonResult(new { found = false, message = "الموظف غير موجود." });
        }

        var template = await ApprovalTemplateStore.ResolveAsync(
            _dbContext, type, employee.BranchId, employee.DepartmentId, employee.WorkType);

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
