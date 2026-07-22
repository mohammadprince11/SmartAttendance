using System.Globalization;

namespace SmartAttendance.Web.Infrastructure.Theming;

/// <summary>
/// Secure storage for company branding assets (Phase P6). Raster only for now —
/// PNG, JPEG, ICO — validated by magic bytes (not just extension), size-capped,
/// and written under wwwroot/tenant-assets/{companyId}/branding/ with generated
/// GUID names. SVG is intentionally excluded until it can be sanitised. All
/// deletes are path-traversal guarded.
/// </summary>
public static class BrandingAssets
{
    public const long MaxAssetBytes = 3 * 1024 * 1024;

    public enum AssetKind
    {
        Logo,
        Favicon,
        LoginBackground,
    }

    public sealed record SaveOutcome(bool Ok, string? WebPath, string? Error);

    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] Ico = { 0x00, 0x00, 0x01, 0x00 };

    public static async Task<SaveOutcome> SaveAsync(
        string webRootPath, int companyId, AssetKind kind, IFormFile file)
    {
        if (file.Length == 0)
        {
            return new SaveOutcome(false, null, "الملف فارغ.");
        }

        if (file.Length > MaxAssetBytes)
        {
            return new SaveOutcome(false, null, "حجم الملف يتجاوز 3MB.");
        }

        var header = new byte[8];
        await using (var input = file.OpenReadStream())
        {
            var read = await input.ReadAsync(header.AsMemory(0, header.Length));
            if (read < 4)
            {
                return new SaveOutcome(false, null, "الملف غير صالح.");
            }
        }

        var extension = DetectExtension(header, kind);
        if (extension is null)
        {
            return new SaveOutcome(false, null, "نوع الصورة غير مدعوم. استخدم PNG أو JPG" + (kind == AssetKind.Favicon ? " أو ICO." : "."));
        }

        var brandingDir = BrandingDirectory(webRootPath, companyId);
        Directory.CreateDirectory(brandingDir);

        var fileName = string.Format(
            CultureInfo.InvariantCulture,
            "{0}_{1}{2}",
            kind.ToString().ToLowerInvariant(),
            Guid.NewGuid().ToString("N"),
            extension);

        var fullPath = Path.Combine(brandingDir, fileName);

        await using (var output = System.IO.File.Create(fullPath))
        {
            await using var source = file.OpenReadStream();
            await source.CopyToAsync(output);
        }

        var webPath = $"/tenant-assets/{companyId}/branding/{fileName}";
        return new SaveOutcome(true, webPath, null);
    }

    /// <summary>Deletes a previously stored asset, guarded against path traversal.</summary>
    public static void Delete(string webRootPath, string? webPath)
    {
        if (string.IsNullOrWhiteSpace(webPath) ||
            !webPath.StartsWith("/tenant-assets/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var assetsRoot = Path.GetFullPath(Path.Combine(webRootPath, "tenant-assets"));
        var relative = webPath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(webRootPath, relative));

        if (!fullPath.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (System.IO.File.Exists(fullPath))
        {
            System.IO.File.Delete(fullPath);
        }
    }

    private static string BrandingDirectory(string webRootPath, int companyId) =>
        Path.Combine(
            webRootPath,
            "tenant-assets",
            companyId.ToString(CultureInfo.InvariantCulture),
            "branding");

    private static string? DetectExtension(byte[] header, AssetKind kind)
    {
        if (StartsWith(header, Png))
        {
            return ".png";
        }

        if (StartsWith(header, Jpeg))
        {
            return ".jpg";
        }

        // ICO only makes sense for favicons.
        if (kind == AssetKind.Favicon && StartsWith(header, Ico))
        {
            return ".ico";
        }

        return null;
    }

    private static bool StartsWith(byte[] data, byte[] prefix)
    {
        if (data.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (data[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }
}
