using System.Text.RegularExpressions;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>Resolves only the configured blank A4 form, never a client-supplied file path.</summary>
public static partial class DisciplinaryFormPreview
{
    public sealed record Asset(string FullPath, string ContentType);

    [GeneratedRegex(@"\A/uploads/disciplinary-forms/a4-form_[0-9]{17}\.(pdf|png|jpg|jpeg|webp)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FormPathPattern();

    public static Asset? Resolve(string webRoot, string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(webRoot) || configuredPath is null || !FormPathPattern().IsMatch(configuredPath)) return null;
        var directory = Path.Combine(Path.GetFullPath(webRoot), "uploads", "disciplinary-forms");
        var fullPath = Path.Combine(directory, Path.GetFileName(configuredPath));
        if (!File.Exists(fullPath)) return null;
        // Do not follow a substituted upload directory or file outside the owned webroot.
        foreach (var path in new[] { Path.GetFullPath(webRoot), Path.Combine(webRoot, "uploads"), directory, fullPath })
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return null;
        var contentType = Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => null
        };
        return contentType is null ? null : new(fullPath, contentType);
    }
}
