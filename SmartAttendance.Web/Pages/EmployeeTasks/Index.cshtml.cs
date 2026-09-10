using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.EmployeeTasks;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyScopeProvider _companyScope;

    public IndexModel(ApplicationDbContext dbContext, ICompanyScopeProvider companyScope)
    {
        _dbContext = dbContext;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)]
    public string StatusFilter { get; set; } = "open";

    [BindProperty(SupportsGet = true)]
    public int ProcessFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public List<TaskRow> Tasks { get; set; } = new();
    public List<HrTaskTemplate> Templates { get; set; } = new();
    public List<EmployeeOption> EmployeeOptions { get; set; } = new();

    public int OpenOnboarding { get; set; }
    public int OpenOffboarding { get; set; }
    public int OverdueCount { get; set; }
    public int DoneThisMonth { get; set; }
    public int PendingLifecycleApprovals { get; set; }
    public List<EmployeeLifecycleApprovalStore.LifecycleRequestRow> LifecycleRequests { get; set; } = new();

    [TempData]
    public string? Message { get; set; }

    private string CurrentUser => User.Identity?.Name ?? "System";

    public async Task OnGetAsync()
    {
        await EmployeeLifecycleApprovalStore.EnsureAsync(_dbContext);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        // كل الإحصاءات والقوائم كانت تشمل موظفي كل الشركات. نحصرها بشركاتي عبر
        // Employee.CompanyId — الأدمن (غير مقيَّد) يبقى شاملاً.
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var allowedCompanies = scope.IsUnrestricted ? null : scope.AllowedCompanyIds.ToHashSet();

        LifecycleRequests = await EmployeeLifecycleApprovalStore.ListAsync(_dbContext, scope);
        PendingLifecycleApprovals = LifecycleRequests.Count(request =>
            request.Status is "Pending" or "Draft" or "Returned" or "WaitingRevision");

        var scoped = _dbContext.EmployeeTasks.AsNoTracking().Where(t => !t.IsDeleted && !t.Employee.IsDeleted);
        if (allowedCompanies is not null)
            scoped = scoped.Where(t => t.Employee.CompanyId != null && allowedCompanies.Contains(t.Employee.CompanyId.Value));

        OpenOnboarding = await scoped.CountAsync(t => !t.IsDone && t.ProcessType == HrProcessType.Onboarding);
        OpenOffboarding = await scoped.CountAsync(t => !t.IsDone && t.ProcessType == HrProcessType.Offboarding);
        OverdueCount = await scoped.CountAsync(t => !t.IsDone && t.DueDate != null && t.DueDate < today);
        DoneThisMonth = await scoped.CountAsync(t => t.IsDone && t.CompletedAt >= monthStart);

        var query = scoped;

        query = StatusFilter switch
        {
            "done" => query.Where(t => t.IsDone),
            "all" => query,
            _ => query.Where(t => !t.IsDone)
        };

        if (ProcessFilter is 1 or 2)
        {
            var process = (HrProcessType)ProcessFilter;
            query = query.Where(t => t.ProcessType == process);
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var s = Search.Trim();
            query = query.Where(t => t.Employee.FullName.Contains(s) || t.Employee.EmployeeNo.Contains(s) || t.Title.Contains(s));
        }

        Tasks = await query
            .OrderBy(t => t.IsDone).ThenBy(t => t.DueDate).ThenBy(t => t.Id)
            .Take(500)
            .Select(t => new TaskRow
            {
                Id = t.Id,
                EmployeeId = t.EmployeeId,
                EmployeeNo = t.Employee.EmployeeNo,
                EmployeeName = t.Employee.FullName,
                ProcessType = t.ProcessType,
                Title = t.Title,
                AssigneeRole = t.AssigneeRole,
                DueDate = t.DueDate,
                IsDone = t.IsDone,
                CompletedBy = t.CompletedBy
            })
            .ToListAsync();

        Templates = await _dbContext.HrTaskTemplates.AsNoTracking()
            .Where(t => !t.IsDeleted)
            .OrderBy(t => t.ProcessType).ThenBy(t => t.SortOrder).ThenBy(t => t.Id)
            .ToListAsync();

        var employeeQuery = _dbContext.Employees.AsNoTracking()
            .Where(e => !e.IsDeleted && e.IsActive);
        if (allowedCompanies is not null)
            employeeQuery = employeeQuery.Where(e => e.CompanyId != null && allowedCompanies.Contains(e.CompanyId.Value));

        EmployeeOptions = await employeeQuery
            .OrderBy(e => e.FullName)
            .Select(e => new EmployeeOption { Id = e.Id, EmployeeNo = e.EmployeeNo, FullName = e.FullName })
            .ToListAsync();
    }

    // ---- Launch a process for an employee ----
    public async Task<IActionResult> OnPostLaunchAsync(int processType, int employeeId, DateOnly? startDate)
    {
        await EmployeeLifecycleApprovalStore.EnsureAsync(_dbContext);

        if (processType is not (1 or 2) || employeeId <= 0)
        {
            Message = "اختر الموظف ونوع العملية.";
            return RedirectToPage();
        }

        // لا تبدأ Lifecycle لموظف خارج شركات المستخدم.
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (!await EmployeeCompanyGuard.CanAccessEmployeeAsync(
                _dbContext, employeeId, scope, HttpContext.RequestAborted))
        {
            return NotFound();
        }

        var employee = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId && !e.IsDeleted);

        if (employee == null)
        {
            Message = "لم يتم العثور على الموظف — اختر من القائمة.";
            return RedirectToPage();
        }

        var process = (HrProcessType)processType;
        var start = startDate ?? DateOnly.FromDateTime(DateTime.Today);

        var result = await EmployeeLifecycleApprovalStore.SubmitAsync(
            _dbContext,
            employee.Id,
            process,
            start,
            CurrentUser);

        Message = result.Message;

        return RedirectToPage(new
        {
            StatusFilter = "all",
            ProcessFilter = processType,
            Search
        });
    }

    public async Task<IActionResult> OnPostResubmitLifecycleAsync(int requestId)
    {
        var result = await EmployeeLifecycleApprovalStore.ResubmitAsync(
            _dbContext,
            await _companyScope.GetAsync(HttpContext.RequestAborted),
            requestId);

        Message = result.Message;
        return RedirectToPage();
    }

    // ---- Task actions ----

    /// <summary>هل مهمة الموظف (<paramref name="id"/>) ضمن نطاق شركاتي؟ الإجراءات
    /// إنجاز/إعادة فتح/حذف تأخذ معرّف المهمة مباشرةً — بلا الحارس تُعدَّل مهامّ شركة أخرى.</summary>
    private async Task<bool> CanAccessTaskAsync(int id) =>
        await EmployeeCompanyGuard.CanAccessOwnedRowAsync(
            _dbContext, EmployeeCompanyGuard.Tables.EmployeeTasks, "Id", id,
            await _companyScope.GetAsync(HttpContext.RequestAborted), HttpContext.RequestAborted);

    public async Task<IActionResult> OnPostCompleteAsync(int id)
    {
        if (!await CanAccessTaskAsync(id)) return NotFound();
        var task = await _dbContext.EmployeeTasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task != null && !task.IsDone)
        {
            task.IsDone = true;
            task.CompletedAt = DateTime.UtcNow;
            task.CompletedBy = CurrentUser;
            await _dbContext.SaveChangesAsync();
            Message = "تم إنجاز المهمة.";
        }

        return RedirectToPage(new { StatusFilter, ProcessFilter, Search });
    }

    public async Task<IActionResult> OnPostReopenAsync(int id)
    {
        if (!await CanAccessTaskAsync(id)) return NotFound();
        var task = await _dbContext.EmployeeTasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task != null && task.IsDone)
        {
            task.IsDone = false;
            task.CompletedAt = null;
            task.CompletedBy = null;
            await _dbContext.SaveChangesAsync();
            Message = "أُعيد فتح المهمة.";
        }

        return RedirectToPage(new { StatusFilter, ProcessFilter, Search });
    }

    public async Task<IActionResult> OnPostDeleteTaskAsync(int id)
    {
        if (!await CanAccessTaskAsync(id)) return NotFound();
        var task = await _dbContext.EmployeeTasks.FirstOrDefaultAsync(t => t.Id == id);
        if (task != null)
        {
            task.IsDeleted = true;
            await _dbContext.SaveChangesAsync();
            Message = "تم حذف المهمة.";
        }

        return RedirectToPage(new { StatusFilter, ProcessFilter, Search });
    }

    // ---- Template management ----
    public async Task<IActionResult> OnPostAddTemplateAsync(int processType, string title, string? assigneeRole, int dueDays, int sortOrder)
    {
        await EmployeeLifecycleApprovalStore.EnsureAsync(_dbContext);

        if (processType is not (1 or 2) || string.IsNullOrWhiteSpace(title))
        {
            Message = "عنوان المهمة مطلوب.";
            return RedirectToPage(null, null, null, "templates");
        }

        _dbContext.HrTaskTemplates.Add(new HrTaskTemplate
        {
            ProcessType = (HrProcessType)processType,
            Title = title.Trim(),
            AssigneeRole = string.IsNullOrWhiteSpace(assigneeRole) ? null : assigneeRole.Trim(),
            DueDays = Math.Max(0, dueDays),
            SortOrder = sortOrder > 0 ? sortOrder : 1000,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = CurrentUser
        });

        await _dbContext.SaveChangesAsync();
        Message = "تمت إضافة القالب.";
        return RedirectToPage(null, null, null, "templates");
    }

    public async Task<IActionResult> OnPostToggleTemplateAsync(int id)
    {
        var template = await _dbContext.HrTaskTemplates.FirstOrDefaultAsync(t => t.Id == id);
        if (template != null)
        {
            template.IsActive = !template.IsActive;
            await _dbContext.SaveChangesAsync();
            Message = "تم تحديث حالة القالب.";
        }

        return RedirectToPage(null, null, null, "templates");
    }

    public async Task<IActionResult> OnPostDeleteTemplateAsync(int id)
    {
        var template = await _dbContext.HrTaskTemplates.FirstOrDefaultAsync(t => t.Id == id);
        if (template != null)
        {
            template.IsDeleted = true;
            await _dbContext.SaveChangesAsync();
            Message = "تم حذف القالب.";
        }

        return RedirectToPage(null, null, null, "templates");
    }

    public string ProcessText(HrProcessType type) =>
        type == HrProcessType.Onboarding ? "تعيين" : "إنهاء";

    public class TaskRow
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public HrProcessType ProcessType { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? AssigneeRole { get; set; }
        public DateOnly? DueDate { get; set; }
        public bool IsDone { get; set; }
        public string? CompletedBy { get; set; }
    }

    public class EmployeeOption
    {
        public int Id { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
    }
}
