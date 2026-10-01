using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Platform;

namespace SmartAttendance.Web.Pages.Platform;

[Authorize(Policy = PlatformAuthenticationDefaults.Policy)]
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    public List<PlatformPortalStore.TenantSummary> Tenants { get; private set; } = [];
    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    public bool HasSearch => !string.IsNullOrWhiteSpace(Search);
    public int ActiveCount => Tenants.Count(item => item.IsActive && item.EffectiveStatus is "فعّالة" or "تجريبية" or "فترة سماح");
    public int TotalEmployees => Tenants.Sum(item => item.EmployeeCount);
    public int ExpiringSoonCount => Tenants.Count(item =>
        item.ExpiresAtUtc.HasValue &&
        item.ExpiresAtUtc.Value >= DateTime.UtcNow &&
        item.ExpiresAtUtc.Value <= DateTime.UtcNow.AddDays(30));

    [TempData]
    public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
        Tenants = await PlatformPortalStore.ListTenantsAsync(_db, Search);
    }
}
