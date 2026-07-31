using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// إزالة كلمة مرور شهادة LAN من المستودع: الفشل يجب أن يكون صريحاً عند الحاجة
/// إليها وغيابها، وصامتاً تماماً حين لا تُستعمل (تشغيل HTTP خلف النفق أو LAN).
/// </summary>
public class CertificateSecretGuardTests
{
    [Fact]
    public void HttpsWithFileAndPassword_IsOk() =>
        Assert.Equal(
            CertificateSecretState.Ok,
            CertificateSecretGuard.Evaluate(true, true, "from-environment"));

    [Fact]
    public void HttpsWithFileButNoPassword_FailsLoudly() =>
        Assert.Equal(
            CertificateSecretState.MissingPassword,
            CertificateSecretGuard.Evaluate(true, true, null));

    [Fact]
    public void WhitespacePassword_CountsAsMissing() =>
        Assert.Equal(
            CertificateSecretState.MissingPassword,
            CertificateSecretGuard.Evaluate(true, true, "   "));

    /// <summary>تشغيل HTTP فقط (النفق/LAN) لا يحتاج الشهادة ⟹ لا يُعطَّل الإقلاع.</summary>
    [Fact]
    public void HttpOnlyRun_DoesNotRequirePassword() =>
        Assert.Equal(
            CertificateSecretState.NotUsed,
            CertificateSecretGuard.Evaluate(false, true, null));

    [Fact]
    public void MissingCertificateFile_DoesNotRequirePassword() =>
        Assert.Equal(
            CertificateSecretState.NotUsed,
            CertificateSecretGuard.Evaluate(true, false, null));

    // ===== اكتشاف نقطة HTTPS =====

    [Fact]
    public void HttpsUrl_IsDetected() =>
        Assert.True(CertificateSecretGuard.HasHttpsEndpoint(
            ["http://localhost:5080", "https://0.0.0.0:5443"]));

    [Fact]
    public void HttpOnlyUrls_AreNotDetectedAsHttps() =>
        Assert.False(CertificateSecretGuard.HasHttpsEndpoint(
            ["http://localhost:5080", null, "   "]));

    [Fact]
    public void EmptyUrlList_IsNotHttps() =>
        Assert.False(CertificateSecretGuard.HasHttpsEndpoint([]));

    /// <summary>الرسالة يجب أن تسمّي المتغيّر المطلوب بالضبط، لا «إعداد ناقص».</summary>
    [Fact]
    public void FailureMessage_NamesTheEnvironmentVariable()
    {
        var message = CertificateSecretGuard.BuildFailureMessage(@"C:\app\certs\lan.pfx");

        Assert.Contains(CertificateSecretGuard.PasswordEnvironmentVariable, message);
        Assert.Contains(@"C:\app\certs\lan.pfx", message);
    }
}
