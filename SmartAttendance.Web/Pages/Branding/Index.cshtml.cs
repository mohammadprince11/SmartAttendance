using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.CompanyContext;
using SmartAttendance.Web.Infrastructure.Theming;

namespace SmartAttendance.Web.Pages.Branding;

/// <summary>
/// Branding Studio (Phase P6): the admin surface of the Branding &amp; Theme
/// Engine. Pick brand colours, upload identity assets (raster only), preview
/// against real components using the real server-side compiler, then run the
/// Draft → Validate → Publish → Rollback lifecycle backed by <see cref="ThemeStore"/>.
/// Admin-only, enforced on every handler (not just the nav link).
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;
    private readonly IThemeContextService _themeContextService;

    public IndexModel(
        ApplicationDbContext dbContext,
        IWebHostEnvironment environment,
        IThemeContextService themeContextService)
    {
        _dbContext = dbContext;
        _environment = environment;
        _themeContextService = themeContextService;
    }

    public const string DefaultPrimary = "#18C7BD";
    public const string DefaultSecondary = "#101C30";
    public const string DefaultAccent = "#D4B36A";

    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;

    public ThemeStore.BrandingProfile Profile { get; set; } = new();
    public List<ThemeStore.ThemeVersion> Versions { get; set; } = new();
    public int? PublishedVersionId { get; set; }

    private bool IsAdmin =>
        (User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty)
        .Equals("Admin", StringComparison.OrdinalIgnoreCase);

    public async Task<IActionResult> OnGetAsync()
    {
        if (!IsAdmin)
        {
            return Forbid();
        }

        var companyId = await ResolveCompanyIdAsync();
        if (companyId is null)
        {
            return RedirectToPage("/Setup/Index");
        }

        await LoadAsync(companyId.Value);
        return Page();
    }

    /// <summary>
    /// AJAX live preview: compiles the supplied colours in memory (no version
    /// row) and returns the CSS + validation so the studio can paint a preview.
    /// </summary>
    public IActionResult OnGetPreview(string primary, string? secondary, string? accent)
    {
        if (!IsAdmin)
        {
            return Forbid();
        }

        var result = ThemeCompiler.Compile(
            new ThemeCompiler.BrandingInput(primary, secondary, accent));

        return new JsonResult(new
        {
            level = result.Level.ToString(),
            messages = result.Messages,
            css = result.CompiledCss,
            onPrimary = result.OnPrimaryHex,
        });
    }

    public async Task<IActionResult> OnPostSaveDraftAsync()
    {
        if (!IsAdmin)
        {
            return Forbid();
        }

        var companyId = await ResolveCompanyIdAsync();
        if (companyId is null)
        {
            return RedirectToPage("/Setup/Index");
        }

        var error = await SaveProfileFromFormAsync(companyId.Value);
        TempData["BrandingMessage"] = error ?? "تم حفظ المسودّة.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPublishAsync()
    {
        if (!IsAdmin)
        {
            return Forbid();
        }

        var companyId = await ResolveCompanyIdAsync();
        if (companyId is null)
        {
            return RedirectToPage("/Setup/Index");
        }

        var error = await SaveProfileFromFormAsync(companyId.Value);
        if (error is not null)
        {
            TempData["BrandingMessage"] = error;
            return RedirectToPage();
        }

        var version = await ThemeStore.CompileDraftAsync(_dbContext, companyId.Value);
        if (version is null)
        {
            TempData["BrandingMessage"] = "لا توجد هوية للنشر.";
            return RedirectToPage();
        }

        if (version.ValidationLevel == ThemeCompiler.ValidationLevel.Block.ToString())
        {
            TempData["BrandingMessage"] = "تعذّر النشر: اللون الرئيسي لا يجتاز فحص التباين.";
            return RedirectToPage();
        }

        await ThemeStore.PublishAsync(_dbContext, companyId.Value, version.Id);
        _themeContextService.Invalidate(companyId.Value);
        TempData["BrandingMessage"] = "تم نشر الهوية بنجاح.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRollbackAsync(int versionId)
    {
        if (!IsAdmin)
        {
            return Forbid();
        }

        var companyId = await ResolveCompanyIdAsync();
        if (companyId is null)
        {
            return RedirectToPage("/Setup/Index");
        }

        var ok = await ThemeStore.PublishAsync(_dbContext, companyId.Value, versionId);
        _themeContextService.Invalidate(companyId.Value);
        TempData["BrandingMessage"] = ok ? "تمت العودة إلى الإصدار المحدّد." : "تعذّرت العودة إلى الإصدار.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteVersionAsync(int versionId)
    {
        if (!IsAdmin)
        {
            return Forbid();
        }

        var companyId = await ResolveCompanyIdAsync();
        if (companyId is null)
        {
            return RedirectToPage("/Setup/Index");
        }

        var wasPublished = await ThemeStore.DeleteVersionAsync(_dbContext, companyId.Value, versionId);
        if (wasPublished)
        {
            _themeContextService.Invalidate(companyId.Value);
        }

        TempData["BrandingMessage"] = wasPublished
            ? "تم حذف الإصدار المنشور — عادت الهوية الافتراضية."
            : "تم حذف الإصدار.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditVersionAsync(int versionId)
    {
        if (!IsAdmin)
        {
            return Forbid();
        }

        var companyId = await ResolveCompanyIdAsync();
        if (companyId is null)
        {
            return RedirectToPage("/Setup/Index");
        }

        var ok = await ThemeStore.LoadVersionIntoProfileAsync(_dbContext, companyId.Value, versionId);
        TempData["BrandingMessage"] = ok
            ? "تم تحميل الإصدار للتعديل — عدّل وانشر."
            : "تعذّر تحميل الإصدار.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetAsync()
    {
        if (!IsAdmin)
        {
            return Forbid();
        }

        var companyId = await ResolveCompanyIdAsync();
        if (companyId is null)
        {
            return RedirectToPage("/Setup/Index");
        }

        await HrmsResetPublishedAsync(companyId.Value);
        _themeContextService.Invalidate(companyId.Value);
        TempData["BrandingMessage"] = "تمت العودة إلى هوية ZYNORA الافتراضية.";
        return RedirectToPage();
    }

    private async Task HrmsResetPublishedAsync(int companyId)
    {
        await SmartAttendance.Web.Infrastructure.Hrms.HrmsDatabase.ExecuteAsync(
            _dbContext,
            "UPDATE ThemeVersions SET Status = 'Archived' WHERE CompanyId = @c AND Status = 'Published';",
            command => SmartAttendance.Web.Infrastructure.Hrms.HrmsDatabase.AddParameter(command, "@c", companyId));
    }

    private async Task<string?> SaveProfileFromFormAsync(int companyId)
    {
        var form = Request.Form;
        var primary = NormalizeHex(form["PrimaryHex"], DefaultPrimary);
        var secondary = NormalizeHex(form["SecondaryHex"], DefaultSecondary);
        var accent = NormalizeHex(form["AccentHex"], DefaultAccent);

        var existing = await ThemeStore.GetBrandingProfileAsync(_dbContext, companyId);

        var profile = new ThemeStore.BrandingProfile
        {
            CompanyId = companyId,
            PrimaryHex = primary,
            SecondaryHex = secondary,
            AccentHex = accent,
            DisplayName = NullIfEmpty(form["DisplayName"]),
            LogoPath = existing?.LogoPath,
            FaviconPath = existing?.FaviconPath,
            LoginBackgroundPath = existing?.LoginBackgroundPath,
        };

        var uploadError = await ApplyUploadsAsync(companyId, profile, existing);
        if (uploadError is not null)
        {
            return uploadError;
        }

        await ThemeStore.SaveBrandingProfileAsync(_dbContext, profile);
        return null;
    }

    private async Task<string?> ApplyUploadsAsync(
        int companyId, ThemeStore.BrandingProfile profile, ThemeStore.BrandingProfile? existing)
    {
        var webRoot = _environment.WebRootPath;

        foreach (var (formKey, kind, assign, previous) in new[]
        {
            ("LogoFile", BrandingAssets.AssetKind.Logo, (Action<string>)(p => profile.LogoPath = p), existing?.LogoPath),
            ("FaviconFile", BrandingAssets.AssetKind.Favicon, p => profile.FaviconPath = p, existing?.FaviconPath),
            ("LoginBackgroundFile", BrandingAssets.AssetKind.LoginBackground, p => profile.LoginBackgroundPath = p, existing?.LoginBackgroundPath),
        })
        {
            var file = Request.Form.Files[formKey];
            if (file is null || file.Length == 0)
            {
                continue;
            }

            var outcome = await BrandingAssets.SaveAsync(webRoot, companyId, kind, file);
            if (!outcome.Ok)
            {
                return outcome.Error;
            }

            assign(outcome.WebPath!);
            BrandingAssets.Delete(webRoot, previous);
        }

        return null;
    }

    private async Task LoadAsync(int companyId)
    {
        CompanyId = companyId;
        CompanyName = await _dbContext.Companies.AsNoTracking()
            .Where(c => c.Id == companyId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync() ?? string.Empty;

        Profile = await ThemeStore.GetBrandingProfileAsync(_dbContext, companyId)
                  ?? new ThemeStore.BrandingProfile
                  {
                      CompanyId = companyId,
                      PrimaryHex = DefaultPrimary,
                      SecondaryHex = DefaultSecondary,
                      AccentHex = DefaultAccent,
                  };

        Versions = await ThemeStore.ListVersionsAsync(_dbContext, companyId);
        PublishedVersionId = Versions
            .FirstOrDefault(v => v.Status == ThemeStore.StatusPublished)?.Id;
    }

    private async Task<int?> ResolveCompanyIdAsync()
    {
        var allowed = await _dbContext.Companies.AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync();

        int? requested = int.TryParse(Request.Query["companyId"], out var q) ? q : null;
        return CompanySelectionContext.Resolve(HttpContext, requested, allowed);
    }

    private static string NormalizeHex(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith('#'))
        {
            trimmed = "#" + trimmed;
        }

        var body = trimmed[1..];
        if ((body.Length == 6 || body.Length == 3) &&
            body.All(Uri.IsHexDigit))
        {
            return trimmed.ToUpperInvariant();
        }

        return fallback;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
