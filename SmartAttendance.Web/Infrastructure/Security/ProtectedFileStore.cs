namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// المرحلة 6 — تخزين ملفات الموظفين الحسّاسة <b>خارج wwwroot</b>
/// (<c>App_Data/ProtectedEmployeeFiles</c>) فلا يخدمها الخادم الثابت إطلاقاً؛
/// الوصول الوحيد عبر نقطة تنزيل مصادَقة تفحص صلاحية الموظف المستهدف.
///
/// المنطق هنا نقي (بلا قاعدة بيانات وبلا HttpContext) ليُختبر كاملاً: توليد اسم
/// تخزين آمن، وتطبيع المسار ورفض أي خروج عن الجذر (path traversal)، وحدود
/// الحجم والامتدادات.
/// </summary>
public static class ProtectedFileStore
{
    public const string RootFolderName = "ProtectedEmployeeFiles";

    public const long MaxFileSizeBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".png", ".jpg", ".jpeg", ".webp",
            ".doc", ".docx", ".xls", ".xlsx"
        };

    public static bool IsAllowedExtension(string? extension) =>
        !string.IsNullOrWhiteSpace(extension) && AllowedExtensions.Contains(extension.Trim());

    public static bool IsAllowedSize(long sizeBytes) =>
        sizeBytes > 0 && sizeBytes <= MaxFileSizeBytes;

    public static string ResolveRoot(string contentRootPath) =>
        Path.Combine(contentRootPath, "App_Data", RootFolderName);

    /// <summary>
    /// مفتاح تخزين آمن مولَّد بالكامل: لا يشتق من اسم الملف المرفوع (فلا يحمل
    /// محارف مسار ولا امتداداً مزدوجاً)، ويعزل كل موظف بمجلده.
    /// </summary>
    public static string BuildStorageKey(int employeeId, string category, string extension)
    {
        var safeCategory = SanitizeSegment(category);
        var safeExtension = IsAllowedExtension(extension)
            ? extension.Trim().ToLowerInvariant()
            : ".bin";

        return $"employee-{employeeId}/{safeCategory}_{Guid.NewGuid():N}{safeExtension}";
    }

    /// <summary>
    /// يستخرج تصنيف مفتاح أنشأه النظام لموظف بعينه. يُستخدم قبل التنزيل لإضافة
    /// صلاحيات خاصة بالتصنيف (مثل الملفات المالية) فوق صلاحية الملف العامة.
    /// </summary>
    public static bool TryGetCategory(string? storageKey, int employeeId, out string category)
    {
        category = string.Empty;
        if (employeeId <= 0 || string.IsNullOrWhiteSpace(storageKey)) return false;

        var key = storageKey.Trim().Replace('\\', '/');
        var prefix = $"employee-{employeeId}/";
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var fileName = key[prefix.Length..];
        var separator = fileName.IndexOf('_');
        if (separator <= 0 || fileName.Contains('/')) return false;

        category = fileName[..separator];
        return category.All(character => char.IsLetterOrDigit(character) || character == '-');
    }

    /// <summary>
    /// يحوّل مفتاح التخزين لمسار فيزيائي ويؤكد بقاءه <b>داخل</b> الجذر المحمي
    /// بعد التطبيع — يرفض <c>..</c> والمسارات المطلقة وأي مفتاح فارغ.
    /// </summary>
    public static bool TryResolvePhysicalPath(
        string root,
        string? storageKey,
        out string physicalPath)
    {
        physicalPath = string.Empty;

        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(storageKey))
        {
            return false;
        }

        var key = storageKey.Trim().Replace('\\', '/');

        if (key.StartsWith('/') || Path.IsPathRooted(key) || key.Contains(':'))
        {
            return false;
        }

        string candidate;
        string rootFull;

        try
        {
            rootFull = Path.GetFullPath(root);
            candidate = Path.GetFullPath(Path.Combine(rootFull, key.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch
        {
            return false;
        }

        var boundary = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        physicalPath = candidate;
        return true;
    }

    /// <summary>يكتب المحتوى تحت الجذر المحمي بعد التحقق من المفتاح.</summary>
    public static async Task<bool> SaveAsync(
        string root,
        string storageKey,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolvePhysicalPath(root, storageKey, out var physicalPath))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(physicalPath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        Directory.CreateDirectory(directory);

        await using var target = File.Create(physicalPath);
        await content.CopyToAsync(target, cancellationToken);
        return true;
    }

    public static bool TryDelete(string root, string? storageKey)
    {
        if (!TryResolvePhysicalPath(root, storageKey, out var physicalPath))
        {
            return false;
        }

        try
        {
            if (File.Exists(physicalPath))
            {
                File.Delete(physicalPath);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>نوع MIME محافظ: نستنتجه من الامتداد لا من ترويسة العميل.</summary>
    public static string ContentTypeFor(string? extension) =>
        (extension ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };

    private static string SanitizeSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "file";
        }

        var cleaned = new string(value
            .Trim()
            .Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')
            .ToArray());

        return cleaned.Length == 0 ? "file" : cleaned[..Math.Min(cleaned.Length, 40)];
    }
}
