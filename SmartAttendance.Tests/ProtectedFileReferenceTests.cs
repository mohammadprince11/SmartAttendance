using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// المرجع المحمي: يميّز الصفوف الجديدة عن التاريخية بلا هجرة مخطط لكل جدول
/// مرفقات، ويفكّ حمولة الرابط الموقّع بأمان (فشل ⟹ رفض، لا تخمين).
/// </summary>
public class ProtectedFileReferenceTests
{
    [Fact]
    public void StoredPath_MarksProtectedRows()
    {
        var stored = ProtectedFileReference.ToStoredPath("employee-5/doc_abc.pdf");

        Assert.True(ProtectedFileReference.IsProtected(stored));
        Assert.Equal("employee-5/doc_abc.pdf", ProtectedFileReference.ExtractKey(stored));
    }

    /// <summary>الصفوف التاريخية تبقى مميَّزة فلا تُعامَل كمفاتيح محمية.</summary>
    [Theory]
    [InlineData("/uploads/employee-documents/id.pdf")]
    [InlineData("")]
    [InlineData(null)]
    public void LegacyPaths_AreNotProtected(string? storedPath)
    {
        Assert.False(ProtectedFileReference.IsProtected(storedPath));
        Assert.Null(ProtectedFileReference.ExtractKey(storedPath));
    }

    [Fact]
    public void Payload_RoundTrips()
    {
        var payload = ProtectedFileReference.EncodePayload(42, "employee-42/record_x.pdf");

        Assert.True(ProtectedFileReference.TryDecodePayload(payload, out var employeeId, out var key));
        Assert.Equal(42, employeeId);
        Assert.Equal("employee-42/record_x.pdf", key);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("no-separator")]
    [InlineData("|key-only")]
    [InlineData("42|")]
    [InlineData("abc|key")]
    [InlineData("0|key")]
    [InlineData("-3|key")]
    public void MalformedPayloads_AreRejected(string? payload) =>
        Assert.False(ProtectedFileReference.TryDecodePayload(payload, out _, out _));

    /// <summary>مفتاح يحوي فاصلاً لا يكسر التحليل (أول فاصل فقط يفصل).</summary>
    [Fact]
    public void KeyContainingSeparator_SurvivesDecoding()
    {
        Assert.True(ProtectedFileReference.TryDecodePayload(
            "7|employee-7/a|b.pdf", out var employeeId, out var key));
        Assert.Equal(7, employeeId);
        Assert.Equal("employee-7/a|b.pdf", key);
    }
}
