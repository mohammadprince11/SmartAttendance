using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Platform;

namespace SmartAttendance.Web.Pages.Platform.Tenants;

[Authorize(Policy = PlatformAuthenticationDefaults.Policy)]
public sealed class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public DetailsModel(ApplicationDbContext db) => _db = db;

    public PlatformPortalStore.TenantSummary Tenant { get; private set; } = null!;
    public IReadOnlyDictionary<string, string> Modules => PlatformPortalStore.ModuleCatalog;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public string[] EnabledModules { get; set; } = [];

    [TempData]
    public string? Message { get; set; }

    public sealed class InputModel
    {
        public int TenantId { get; set; }
        public string VersionToken { get; set; } = string.Empty;

        [Required, StringLength(60, MinimumLength = 2)]
        public string PlanCode { get; set; } = string.Empty;

        [Required]
        public string LicenseStatus { get; set; } = "Active";

        [DataType(DataType.Date)]
        public DateTime StartsAt { get; set; }

        [DataType(DataType.Date)]
        public DateTime? ExpiresAt { get; set; }

        [DataType(DataType.Date)]
        public DateTime? GraceEndsAt { get; set; }

        [Range(1, 1000)] public int MaxCompanies { get; set; }
        [Range(1, 1000000)] public int MaxEmployees { get; set; }
        [Range(0, 100000)] public int MaxDevices { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var tenant = await PlatformPortalStore.GetTenantAsync(_db, id);
        if (tenant is null) return NotFound();

        Tenant = tenant;
        Input = new InputModel
        {
            TenantId = tenant.Id,
            VersionToken = tenant.VersionToken,
            PlanCode = tenant.PlanCode,
            LicenseStatus = tenant.LicenseStatus,
            StartsAt = tenant.StartsAtUtc.Date,
            ExpiresAt = tenant.ExpiresAtUtc?.Date,
            GraceEndsAt = tenant.GraceEndsAtUtc?.Date,
            MaxCompanies = tenant.MaxCompanies,
            MaxEmployees = tenant.MaxEmployees,
            MaxDevices = tenant.MaxDevices
        };
        EnabledModules = tenant.EnabledModules.ToArray();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveLicenseAsync()
    {
        if (!TryDecodeVersion(Input.VersionToken, out var version) ||
            !PlatformLicensePolicy.AllowedStatuses.Contains(Input.LicenseStatus, StringComparer.Ordinal) ||
            Input.ExpiresAt.HasValue && Input.ExpiresAt.Value.Date < Input.StartsAt.Date ||
            Input.GraceEndsAt.HasValue && (!Input.ExpiresAt.HasValue || Input.GraceEndsAt.Value.Date < Input.ExpiresAt.Value.Date) ||
            EnabledModules.Length == 0 || EnabledModules.Any(module => !Modules.ContainsKey(module)))
        {
            ModelState.AddModelError(string.Empty, "تحقق من بيانات اللايسنس قبل الحفظ.");
        }

        if (!ModelState.IsValid)
        {
            var reload = await PlatformPortalStore.GetTenantAsync(_db, Input.TenantId);
            if (reload is null) return NotFound();
            Tenant = reload;
            return Page();
        }

        var updated = await PlatformPortalStore.UpdateLicenseAsync(
            _db,
            new PlatformPortalStore.UpdateLicenseCommand(
                Input.TenantId,
                Input.PlanCode,
                Input.LicenseStatus,
                DateTime.SpecifyKind(Input.StartsAt.Date, DateTimeKind.Utc),
                Input.ExpiresAt.HasValue ? DateTime.SpecifyKind(Input.ExpiresAt.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc) : null,
                Input.GraceEndsAt.HasValue ? DateTime.SpecifyKind(Input.GraceEndsAt.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc) : null,
                Input.MaxCompanies,
                Input.MaxEmployees,
                Input.MaxDevices,
                EnabledModules,
                version),
            User.Identity?.Name ?? "Unknown",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        if (!updated)
        {
            Message = "لم يُحفظ التعديل لأن اللايسنس تغير من جلسة أخرى. راجع القيم ثم أعد المحاولة.";
        }
        else
        {
            Message = "تم تحديث اللايسنس بنجاح.";
        }

        return RedirectToPage(new { id = Input.TenantId });
    }

    public async Task<IActionResult> OnPostSetActiveAsync(int id, bool active)
    {
        await PlatformPortalStore.SetTenantActiveAsync(
            _db,
            id,
            active,
            User.Identity?.Name ?? "Unknown",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        Message = active ? "تم تفعيل المنظومة." : "تم إيقاف المنظومة وإسقاط وصولها.";
        return RedirectToPage(new { id });
    }

    private static bool TryDecodeVersion(string? token, out byte[] version)
    {
        try
        {
            version = Convert.FromBase64String(token ?? string.Empty);
            return version.Length == 8;
        }
        catch (FormatException)
        {
            version = [];
            return false;
        }
    }
}
