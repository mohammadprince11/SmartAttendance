using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// خدمة واحدة تخدم كل موديولات المرفقات الحسّاسة: تحفظ الملف خارج <c>wwwroot</c>
/// وترجع قيمة العمود (<c>protected:{key}</c>)، وتبني رابط التنزيل الموقّع للعرض.
///
/// الهدف أن يصبح تحويل أي موديول سطرين: استبدال كتابة الملف بـ<see cref="SaveAsync"/>،
/// واستبدال <c>href="@row.StoredPath"</c> بـ<see cref="BuildUrl"/>.
/// </summary>
public interface IProtectedFileService
{
    /// <summary>يحفظ المرفق ويرجع قيمة تُخزَّن بعمود المسار، أو null إن رُفض الملف.</summary>
    Task<string?> SaveAsync(
        IFormFile? file,
        int employeeId,
        string category,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// رابط الفتح: نقطة موقّعة للصفوف المحمية، والمسار كما هو للصفوف التاريخية
    /// (لم تعد عامة — تمرّ بمصادقة الحارس ودور الباك أوفيس).
    /// </summary>
    string BuildUrl(int employeeId, string? storedPath);

    void TryDelete(string? storedPath);
}

public sealed class ProtectedFileService : IProtectedFileService
{
    // اسم ثابت: تغييره يُبطل كل الروابط المُصدَرة سابقاً.
    private const string ProtectorPurpose = "ZYNORA.ProtectedEmployeeFile.v1";

    private readonly IDataProtector _protector;
    private readonly IWebHostEnvironment _environment;

    public ProtectedFileService(IDataProtectionProvider provider, IWebHostEnvironment environment)
    {
        _protector = provider.CreateProtector(ProtectorPurpose);
        _environment = environment;
    }

    private string Root => ProtectedFileStore.ResolveRoot(_environment.ContentRootPath);

    public async Task<string?> SaveAsync(
        IFormFile? file,
        int employeeId,
        string category,
        CancellationToken cancellationToken = default)
    {
        if (file is null || employeeId <= 0 || !ProtectedFileStore.IsAllowedSize(file.Length))
        {
            return null;
        }

        var extension = Path.GetExtension(file.FileName);

        if (!ProtectedFileStore.IsAllowedExtension(extension))
        {
            return null;
        }

        // التوقيع الفعلي للملف يُفحص أيضاً: الامتداد وحده يُزوَّر بإعادة التسمية.
        if (!await UploadSignatureValidator.IsValidForExtensionAsync(file, extension))
        {
            return null;
        }

        var storageKey = ProtectedFileStore.BuildStorageKey(employeeId, category, extension);

        await using var source = file.OpenReadStream();
        var stored = await ProtectedFileStore.SaveAsync(Root, storageKey, source, cancellationToken);

        return stored ? ProtectedFileReference.ToStoredPath(storageKey) : null;
    }

    public string BuildUrl(int employeeId, string? storedPath)
    {
        var key = ProtectedFileReference.ExtractKey(storedPath);

        if (key is null)
        {
            return storedPath ?? string.Empty;
        }

        var token = _protector.Protect(
            ProtectedFileReference.EncodePayload(employeeId, key));

        return "/files/download?t=" + Uri.EscapeDataString(token);
    }

    public void TryDelete(string? storedPath)
    {
        var key = ProtectedFileReference.ExtractKey(storedPath);

        if (key is not null)
        {
            ProtectedFileStore.TryDelete(Root, key);
        }
    }

    /// <summary>يفكّ الحمولة الموقّعة. الفشل = عبث أو مفتاح حماية مختلف ⟹ رفض.</summary>
    public bool TryUnprotect(string? token, out int employeeId, out string storageKey)
    {
        employeeId = 0;
        storageKey = string.Empty;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            return ProtectedFileReference.TryDecodePayload(
                _protector.Unprotect(token), out employeeId, out storageKey);
        }
        catch
        {
            return false;
        }
    }

    public string ResolveRoot() => Root;
}
