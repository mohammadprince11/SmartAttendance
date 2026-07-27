using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات المنطق النقي لمفاتيح WebAuthn: ترميز Base64Url، دورة حياة الحالات
/// (مفتاح نشط واحد، البتّ بالمعلّق فقط)، ونافذة الإثبات البيولوجي أحادي الاستهلاك.
/// </summary>
public class WebAuthnCredentialPolicyTests
{
    // ===== Base64Url =====

    [Theory]
    [InlineData(new byte[] { 0xFA, 0xEF, 0xBE })]              // يولّد + و/ بالـBase64 العادي
    [InlineData(new byte[] { 1 })]                             // حشو مزدوج ==
    [InlineData(new byte[] { 1, 2 })]                          // حشو مفرد =
    [InlineData(new byte[] { 1, 2, 3 })]                       // بلا حشو
    public void Base64Url_RoundTrip(byte[] bytes)
    {
        var encoded = WebAuthnCredentialStore.ToBase64Url(bytes);
        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.DoesNotContain('=', encoded);
        Assert.Equal(bytes, WebAuthnCredentialStore.FromBase64Url(encoded));
    }

    // ===== دورة حياة الحالات =====

    [Theory]
    [InlineData(WebAuthnCredentialStore.StatusActive, true)]
    [InlineData(WebAuthnCredentialStore.StatusPending, false)]
    [InlineData(WebAuthnCredentialStore.StatusRejected, false)]
    [InlineData(WebAuthnCredentialStore.StatusSuperseded, false)]
    [InlineData(WebAuthnCredentialStore.StatusRevoked, false)]
    public void IsUsable_OnlyActive(string status, bool expected) =>
        Assert.Equal(expected, WebAuthnCredentialStore.IsUsable(status));

    [Theory]
    [InlineData(WebAuthnCredentialStore.StatusPending, true)]
    [InlineData(WebAuthnCredentialStore.StatusActive, true)]
    [InlineData(WebAuthnCredentialStore.StatusRejected, false)]
    [InlineData(WebAuthnCredentialStore.StatusSuperseded, false)]
    [InlineData(WebAuthnCredentialStore.StatusRevoked, false)]
    public void NewRegistration_SupersedesPendingAndActiveOnly(string status, bool expected) =>
        Assert.Equal(expected, WebAuthnCredentialStore.SupersededByNewRegistration(status));

    [Theory]
    [InlineData(WebAuthnCredentialStore.StatusPending, true)]
    [InlineData(WebAuthnCredentialStore.StatusActive, false)]
    [InlineData(WebAuthnCredentialStore.StatusRejected, false)]
    public void CanDecide_PendingOnly(string status, bool expected) =>
        Assert.Equal(expected, WebAuthnCredentialStore.CanDecide(status));

    [Fact]
    public void StatusArabic_KnownStatuses_HaveLabels()
    {
        Assert.Equal("بانتظار الاعتماد", WebAuthnCredentialStore.StatusArabic(WebAuthnCredentialStore.StatusPending));
        Assert.Equal("نشط", WebAuthnCredentialStore.StatusArabic(WebAuthnCredentialStore.StatusActive));
        Assert.Equal("مُلغى", WebAuthnCredentialStore.StatusArabic(WebAuthnCredentialStore.StatusRevoked));
    }

    // ===== الإثبات البيولوجي =====

    private static readonly DateTime Now = new(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Proof_FreshAndSameEmployee_Valid() =>
        Assert.True(WebAuthnProofStore.IsValid((7, Now), 7, Now.AddSeconds(30), TimeSpan.FromMinutes(2)));

    [Fact]
    public void Proof_OtherEmployee_Invalid() =>
        Assert.False(WebAuthnProofStore.IsValid((7, Now), 8, Now.AddSeconds(30), TimeSpan.FromMinutes(2)));

    [Fact]
    public void Proof_Expired_Invalid() =>
        Assert.False(WebAuthnProofStore.IsValid((7, Now), 7, Now.AddMinutes(3), TimeSpan.FromMinutes(2)));

    [Fact]
    public void Proof_IssuedInFuture_Invalid() =>
        Assert.False(WebAuthnProofStore.IsValid((7, Now), 7, Now.AddSeconds(-1), TimeSpan.FromMinutes(2)));

    [Fact]
    public void Proof_ConsumeOnce_SecondUseRejected()
    {
        var token = WebAuthnProofStore.Issue(42);
        Assert.True(WebAuthnProofStore.Consume(token, 42));
        Assert.False(WebAuthnProofStore.Consume(token, 42));
    }

    [Fact]
    public void Proof_ConsumeByOtherEmployee_Rejected()
    {
        var token = WebAuthnProofStore.Issue(42);
        Assert.False(WebAuthnProofStore.Consume(token, 43));
        // الاستهلاك الفاشل يحرق التوكن أيضاً (لا يبقى قابلاً للسرقة)
        Assert.False(WebAuthnProofStore.Consume(token, 42));
    }

    [Fact]
    public void Proof_MissingToken_Rejected()
    {
        Assert.False(WebAuthnProofStore.Consume(null, 42));
        Assert.False(WebAuthnProofStore.Consume("", 42));
        Assert.False(WebAuthnProofStore.Consume("not-a-token", 42));
    }
}
