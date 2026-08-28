using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Integrations;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Integrations;

public sealed class AccountingModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;
    public AccountingModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)] public int? CompanyId { get; set; }
    public List<CompanyOption> Companies { get; private set; } = [];
    public List<AccountingMappingStore.Mapping> Mappings { get; private set; } = [];
    public IReadOnlyList<string> Roles => AccountingJournalAdapter.RequiredRoles;

    public string Label(string role) => role switch
    {
        AccountingJournalAdapter.PayrollExpense => "مصروف الرواتب",
        AccountingJournalAdapter.GosiExpense => "مصروف ضمان الشركة",
        AccountingJournalAdapter.TaxPayable => "ضريبة مستحقة",
        AccountingJournalAdapter.GosiPayable => "ضمان مستحق",
        AccountingJournalAdapter.OtherDeductionPayable => "استقطاعات أخرى مستحقة",
        AccountingJournalAdapter.NetPayable => "صافي رواتب مستحق",
        _ => role
    };

    public async Task<IActionResult> OnGetAsync()
    {
        var scope = await _companyScope.GetAsync();
        await LoadCompaniesAsync(scope);
        if (CompanyId is > 0 && !scope.Allows(CompanyId.Value)) return Forbid();
        if (CompanyId is null && Companies.Count == 1) CompanyId = Companies[0].Id;
        if (CompanyId is > 0) Mappings = await AccountingMappingStore.ListAsync(_db, scope, CompanyId.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(
        int companyId, string role, string accountCode, string accountName)
    {
        var scope = await _companyScope.GetAsync();
        if (!scope.Allows(companyId)) return Forbid();
        if (string.IsNullOrWhiteSpace(accountCode) || string.IsNullOrWhiteSpace(accountName))
        {
            TempData["ErrorMessage"] = "رمز الحساب واسمه مطلوبان.";
            return RedirectToPage(new { CompanyId = companyId });
        }
        await AccountingMappingStore.SaveAsync(
            _db, scope, companyId, role, accountCode, accountName);
        TempData["SuccessMessage"] = "حُفظ ربط الحساب.";
        return RedirectToPage(new { CompanyId = companyId });
    }

    private async Task LoadCompaniesAsync(CompanyScope scope)
    {
        var query = _db.Companies.AsNoTracking().Where(company => company.IsActive && !company.IsDeleted);
        if (!scope.IsUnrestricted)
        {
            var allowed = scope.AllowedCompanyIds.ToArray();
            query = query.Where(company => allowed.Contains(company.Id));
        }
        Companies = await query.OrderBy(company => company.Name)
            .Select(company => new CompanyOption(company.Id, company.Name)).ToListAsync();
    }
    public sealed record CompanyOption(int Id, string Name);
}
