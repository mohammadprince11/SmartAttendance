using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Platform;

namespace SmartAttendance.Web.Pages.Platform.Tenants;

[Authorize(Policy = PlatformAuthenticationDefaults.Policy)]
public sealed class CreateModel : PageModel
{
    private const string DefaultPlanCode = "Custom";
    private readonly ApplicationDbContext _db;

    public CreateModel(ApplicationDbContext db) => _db = db;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public string[] EnabledModules { get; set; } = PlatformPortalStore.ModuleCatalog.Keys.ToArray();

    public IReadOnlyDictionary<string, string> Modules => PlatformPortalStore.ModuleCatalog;

    public sealed class InputModel
    {
        [Required, StringLength(200, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [StringLength(240)]
        public string? LegalName { get; set; }

        [Required, StringLength(160, MinimumLength = 2)]
        public string ContactName { get; set; } = string.Empty;

        [Required, EmailAddress, StringLength(254)]
        public string ContactEmail { get; set; } = string.Empty;

        [Required, Phone, StringLength(40, MinimumLength = 5)]
        public string ContactPhone { get; set; } = string.Empty;

        [Required, StringLength(100, MinimumLength = 2)]
        public string Country { get; set; } = "العراق";

        [StringLength(500)]
        public string? Address { get; set; }

        [StringLength(80)]
        public string? TaxNumber { get; set; }

        [Required, RegularExpression("^[A-Za-z0-9._-]{3,100}$")]
        public string AdminUsername { get; set; } = "admin";

        [Required, StringLength(200, MinimumLength = 8)]
        public string AdminPassword { get; set; } = string.Empty;

        [Required]
        public string LicenseStatus { get; set; } = "Trial";

        [DataType(DataType.Date)]
        public DateTime StartsAt { get; set; } = DateTime.Today;

        [DataType(DataType.Date)]
        public DateTime? ExpiresAt { get; set; } = DateTime.Today.AddDays(14);

        [DataType(DataType.Date)]
        public DateTime? GraceEndsAt { get; set; } = DateTime.Today.AddDays(21);

        [Range(1, 1000)]
        public int MaxCompanies { get; set; } = 3;

        [Range(1, 1000000)]
        public int MaxEmployees { get; set; } = 500;

        [Range(0, 100000)]
        public int MaxDevices { get; set; } = 10;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!PlatformLicensePolicy.AllowedStatuses.Contains(Input.LicenseStatus, StringComparer.Ordinal) ||
            Input.ExpiresAt.HasValue && Input.ExpiresAt.Value.Date < Input.StartsAt.Date ||
            Input.GraceEndsAt.HasValue && (!Input.ExpiresAt.HasValue || Input.GraceEndsAt.Value.Date < Input.ExpiresAt.Value.Date) ||
            EnabledModules.Length == 0 || EnabledModules.Any(module => !Modules.ContainsKey(module)))
        {
            ModelState.AddModelError(string.Empty, "تحقق من حالة اللايسنس والتواريخ والمودلات المختارة.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await PlatformPortalStore.CreateTenantAsync(
            _db,
            new PlatformPortalStore.CreateTenantCommand(
                Input.Name,
                Input.LegalName,
                Input.ContactName,
                Input.ContactEmail,
                Input.ContactPhone,
                Input.Country,
                Input.Address,
                Input.TaxNumber,
                Input.AdminUsername,
                Input.AdminPassword,
                DefaultPlanCode,
                Input.LicenseStatus,
                DateTime.SpecifyKind(Input.StartsAt.Date, DateTimeKind.Utc),
                Input.ExpiresAt.HasValue ? DateTime.SpecifyKind(Input.ExpiresAt.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc) : null,
                Input.GraceEndsAt.HasValue ? DateTime.SpecifyKind(Input.GraceEndsAt.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc) : null,
                Input.MaxCompanies,
                Input.MaxEmployees,
                Input.MaxDevices,
                EnabledModules),
            User.Identity?.Name ?? "Unknown",
            HttpContext.Connection.RemoteIpAddress?.ToString());

        TempData["Message"] = $"تم إنشاء منظومة {Input.Name} بالكود {result.Code}.";
        return RedirectToPage("/Platform/Tenants/Details", new { id = result.TenantId });
    }

}
