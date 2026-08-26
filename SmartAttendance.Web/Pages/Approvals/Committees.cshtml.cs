using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.CompanyContext;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Approvals;

[Authorize(Roles = "Admin")]
public sealed class CommitteesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;

    public CommitteesModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)] public int? CompanyId { get; set; }
    public List<Option> Companies { get; private set; } = new();
    public List<string> Users { get; private set; } = new();
    public List<ApprovalCommitteeStore.GroupRow> Groups { get; private set; } = new();
    public List<ApprovalCommitteeStore.ExternalRow> ExternalCommittees { get; private set; } = new();
    [TempData] public string? Message { get; set; }
    [TempData] public bool MessageIsError { get; set; }

    public sealed record Option(int Id, string Name);

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostSaveGroupAsync(
        int id, string name, string? description, List<string> members)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (CompanyId is not > 0 || !scope.Allows(CompanyId.Value)) return Forbid();
        try
        {
            await ApprovalCommitteeStore.SaveGroupAsync(
                _db, scope, CompanyId.Value, id, name, description, members, Actor);
            Message = id > 0 ? "تم تحديث مجموعة اللجنة." : "تم إنشاء مجموعة اللجنة.";
        }
        catch (ArgumentException exception)
        {
            Message = exception.Message;
            MessageIsError = true;
        }
        return RedirectToPage(new { CompanyId });
    }

    public async Task<IActionResult> OnPostDeactivateGroupAsync(int id)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (CompanyId is not > 0 || !scope.Allows(CompanyId.Value)) return Forbid();
        await ApprovalCommitteeStore.DeactivateGroupAsync(_db, scope, CompanyId.Value, id);
        Message = "تم إيقاف مجموعة اللجنة، والقوالب والطلبات التاريخية محفوظة.";
        return RedirectToPage(new { CompanyId });
    }

    public async Task<IActionResult> OnPostSaveExternalAsync(
        int id, string name, string? contactName, string? contactEmail, string? notes)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (CompanyId is not > 0 || !scope.Allows(CompanyId.Value)) return Forbid();
        try
        {
            await ApprovalCommitteeStore.SaveExternalAsync(_db, scope, new ApprovalCommitteeStore.ExternalRow
            {
                Id = id, CompanyId = CompanyId.Value, Name = name,
                ContactName = contactName, ContactEmail = contactEmail, Notes = notes, IsActive = true
            }, Actor);
            Message = id > 0 ? "تم تحديث اللجنة الخارجية." : "تم إنشاء اللجنة الخارجية.";
        }
        catch (ArgumentException exception)
        {
            Message = exception.Message;
            MessageIsError = true;
        }
        return RedirectToPage(new { CompanyId });
    }

    public async Task<IActionResult> OnPostDeactivateExternalAsync(int id)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (CompanyId is not > 0 || !scope.Allows(CompanyId.Value)) return Forbid();
        await ApprovalCommitteeStore.DeactivateExternalAsync(_db, scope, CompanyId.Value, id);
        Message = "تم إيقاف اللجنة الخارجية.";
        return RedirectToPage(new { CompanyId });
    }

    private string Actor => User.Identity?.Name ?? "Admin";

    private async Task LoadAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        Companies = (await _db.Companies.AsNoTracking().Where(company => !company.IsDeleted && company.IsActive)
                .OrderBy(company => company.Name).Select(company => new Option(company.Id, company.Name)).ToListAsync())
            .Where(company => scope.Allows(company.Id)).ToList();
        CompanyId = CompanySelectionContext.Resolve(HttpContext, CompanyId, Companies.Select(company => company.Id).ToArray());
        if (CompanyId is not > 0 || !scope.Allows(CompanyId.Value)) return;

        Users = await _db.SystemUsers.AsNoTracking()
            .Where(user => !user.IsDeleted && user.IsActive && user.Employee != null && user.Employee.CompanyId == CompanyId)
            .OrderBy(user => user.UserName).Select(user => user.UserName).ToListAsync();
        Groups = await ApprovalCommitteeStore.ListGroupsAsync(_db, scope, CompanyId.Value);
        ExternalCommittees = await ApprovalCommitteeStore.ListExternalAsync(_db, scope, CompanyId.Value);
    }
}
