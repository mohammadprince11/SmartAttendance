using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حرّاس سياسة CSP — بنية nonce خلف الراية <c>Security:StrictCsp</c> (Phase 11):
/// الافتراضي يبقى متساهلاً (<c>'unsafe-inline'</c>) فلا ينكسر الحيّ؛ والوضع الصارم
/// يستبدل بـ <c>'nonce-…'</c> فيُعطّل المضمّن غير الحامل للـnonce.
/// </summary>
public sealed class CspPolicyTests
{
    [Fact]
    public void DefaultPolicy_KeepsUnsafeInline_ForBackwardCompatibility()
    {
        var csp = SecurityHeaderPolicy.BuildContentSecurityPolicy();
        Assert.Contains("script-src 'self' 'unsafe-inline'", csp);
        Assert.Contains("style-src 'self' 'unsafe-inline'", csp);
    }

    [Fact]
    public void StrictPolicy_UsesNonce_AndDropsUnsafeInline()
    {
        var csp = SecurityHeaderPolicy.BuildContentSecurityPolicy("abc123==");
        Assert.Contains("script-src 'self' 'nonce-abc123=='", csp);
        Assert.Contains("style-src 'self' 'nonce-abc123=='", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
        // بقيّة التوجيهات المشتركة تبقى (قفل الأصل والتأطير).
        Assert.Contains("frame-ancestors 'self'", csp);
        Assert.Contains("object-src 'none'", csp);
    }

    [Fact]
    public void NewNonce_IsNonEmptyAndUnique()
    {
        var a = SecurityHeaderPolicy.NewNonce();
        var b = SecurityHeaderPolicy.NewNonce();
        Assert.False(string.IsNullOrWhiteSpace(a));
        Assert.NotEqual(a, b);
    }
}
