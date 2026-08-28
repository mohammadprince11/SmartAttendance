using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Integrations;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Integrations;

public sealed class DeviceConnectorsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;
    public DeviceConnectorsModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)] public int? CompanyId { get; set; }
    public List<CompanyOption> Companies { get; private set; } = [];
    public List<IntegrationApiKeyStore.KeyInfo> Keys { get; private set; } = [];
    public List<DevicePunchInboxStore.Heartbeat> Heartbeats { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var scope = await _companyScope.GetAsync();
        await LoadCompaniesAsync(scope);
        if (CompanyId is > 0 && !scope.Allows(CompanyId.Value)) return Forbid();
        if (CompanyId is null && Companies.Count == 1) CompanyId = Companies[0].Id;
        if (CompanyId is > 0)
        {
            Keys = await IntegrationApiKeyStore.ListAsync(_db, scope, CompanyId.Value);
            Heartbeats = await DevicePunchInboxStore.ListHeartbeatsAsync(_db, scope, CompanyId.Value);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostIssueKeyAsync(int companyId, string name, DateTime? expiresAt)
    {
        var scope = await _companyScope.GetAsync();
        if (!scope.Allows(companyId)) return Forbid();
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["ErrorMessage"] = "اسم المفتاح مطلوب.";
            return RedirectToPage(new { CompanyId = companyId });
        }
        var token = await IntegrationApiKeyStore.IssueAsync(
            _db, scope, companyId, name, "attendance.write", expiresAt);
        TempData["IntegrationKey"] = token;
        TempData["SuccessMessage"] = "صدر مفتاح الجهاز. انسخه الآن؛ لن يظهر ثانية.";
        return RedirectToPage(new { CompanyId = companyId });
    }

    public async Task<IActionResult> OnPostRevokeKeyAsync(int companyId, int id)
    {
        var scope = await _companyScope.GetAsync();
        if (!scope.Allows(companyId)) return Forbid();
        await IntegrationApiKeyStore.RevokeAsync(_db, scope, companyId, id);
        TempData["SuccessMessage"] = "أُلغي المفتاح فوراً.";
        return RedirectToPage(new { CompanyId = companyId });
    }

    public async Task<IActionResult> OnPostRetryDeadLettersAsync(int companyId, string? connectorKey)
    {
        var scope = await _companyScope.GetAsync();
        if (!scope.Allows(companyId)) return Forbid();
        await DevicePunchInboxStore.RetryDeadLettersAsync(_db, scope, companyId, connectorKey);
        TempData["SuccessMessage"] = "أعيدت العناصر الميتة إلى طابور المعالجة.";
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
