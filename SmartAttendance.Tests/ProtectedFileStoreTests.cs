using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// المرحلة 6 — تخزين ملفات الموظفين خارج wwwroot: رفض الخروج عن الجذر
/// (path traversal)، حدود الحجم والامتداد، وأسماء تخزين مولَّدة لا مشتقة من
/// اسم الملف المرفوع.
/// </summary>
public class ProtectedFileStoreTests
{
    private static readonly string Root =
        Path.Combine(Path.GetTempPath(), "zynora-protected-tests");

    // ===== تطبيع المسار =====

    [Fact]
    public void ValidKey_ResolvesInsideRoot()
    {
        Assert.True(ProtectedFileStore.TryResolvePhysicalPath(
            Root, "employee-5/Medical_abc.pdf", out var path));
        Assert.StartsWith(Path.GetFullPath(Root), path, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../../../Windows/win.ini")]
    [InlineData("employee-5/../../secrets.txt")]
    [InlineData("..\\..\\appsettings.json")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("")]
    [InlineData(null)]
    public void TraversalAndAbsolutePaths_AreRejected(string? key) =>
        Assert.False(ProtectedFileStore.TryResolvePhysicalPath(Root, key, out _));

    /// <summary>مجلد شقيق يبدأ بنفس النص لا يُعدّ داخل الجذر.</summary>
    [Fact]
    public void SiblingDirectoryWithSharedPrefix_IsRejected() =>
        Assert.False(ProtectedFileStore.TryResolvePhysicalPath(
            Root, "../zynora-protected-tests-other/file.pdf", out _));

    // ===== الامتدادات والحجم =====

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".PNG")]
    [InlineData(".docx")]
    public void AllowedExtensions_Accepted(string extension) =>
        Assert.True(ProtectedFileStore.IsAllowedExtension(extension));

    [Theory]
    [InlineData(".exe")]
    [InlineData(".dll")]
    [InlineData(".svg")]
    [InlineData("")]
    [InlineData(null)]
    public void DangerousOrUnknownExtensions_Rejected(string? extension) =>
        Assert.False(ProtectedFileStore.IsAllowedExtension(extension));

    [Fact]
    public void OversizedUpload_Rejected() =>
        Assert.False(ProtectedFileStore.IsAllowedSize(ProtectedFileStore.MaxFileSizeBytes + 1));

    [Fact]
    public void EmptyUpload_Rejected() => Assert.False(ProtectedFileStore.IsAllowedSize(0));

    [Fact]
    public void SizeAtLimit_Accepted() =>
        Assert.True(ProtectedFileStore.IsAllowedSize(ProtectedFileStore.MaxFileSizeBytes));

    // ===== مفتاح التخزين =====

    [Fact]
    public void StorageKey_IsGenerated_NotDerivedFromUploadedName()
    {
        var key = ProtectedFileStore.BuildStorageKey(7, "Medical/../..", ".pdf");

        Assert.StartsWith("employee-7/", key);
        Assert.DoesNotContain("..", key);
        Assert.DoesNotContain("/", key["employee-7/".Length..]);
        Assert.EndsWith(".pdf", key);
        Assert.True(ProtectedFileStore.TryResolvePhysicalPath(Root, key, out _));
    }

    [Fact]
    public void StorageKey_IsUniquePerCall() =>
        Assert.NotEqual(
            ProtectedFileStore.BuildStorageKey(1, "Medical", ".pdf"),
            ProtectedFileStore.BuildStorageKey(1, "Medical", ".pdf"));

    [Fact]
    public void StorageKey_UnknownExtension_FallsBackToBinary() =>
        Assert.EndsWith(".bin", ProtectedFileStore.BuildStorageKey(1, "Medical", ".exe"));

    [Fact]
    public void EmployeesAreIsolatedByFolder() =>
        Assert.StartsWith("employee-42/", ProtectedFileStore.BuildStorageKey(42, "Training", ".pdf"));

    // ===== نوع المحتوى =====

    [Fact]
    public void ContentType_ComesFromExtension_NotClientHeader() =>
        Assert.Equal("application/pdf", ProtectedFileStore.ContentTypeFor(".PDF"));

    [Fact]
    public void ContentType_UnknownExtension_IsOpaqueDownload() =>
        Assert.Equal("application/octet-stream", ProtectedFileStore.ContentTypeFor(".docx"));

    [Fact]
    public void ProtectedRoot_IsOutsideWwwroot()
    {
        var root = ProtectedFileStore.ResolveRoot(@"C:\app");

        Assert.DoesNotContain("wwwroot", root, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("App_Data", root, StringComparison.OrdinalIgnoreCase);
    }
}
