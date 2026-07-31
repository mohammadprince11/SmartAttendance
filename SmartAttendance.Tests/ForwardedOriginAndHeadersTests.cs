using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// المرحلة 10 — اشتقاق أصل WebAuthn من بيانات مطبَّعة + ترويسات الأمان وسياسة
/// المحتوى. الخلل السابق: قراءة X-Forwarded-Proto الخام من أي عميل.
/// </summary>
public class ForwardedOriginResolverTests
{
    private static readonly string[] Allowed = ["portal.zynorahr.com", "localhost"];

    [Fact]
    public void TunnelHttpsOrigin_IsResolved() =>
        Assert.Equal(
            "https://portal.zynorahr.com",
            ForwardedOriginResolver.Resolve("https", "portal.zynorahr.com", Allowed));

    [Fact]
    public void LocalDevelopmentWithPort_IsResolved() =>
        Assert.Equal(
            "http://localhost:5080",
            ForwardedOriginResolver.Resolve("http", "localhost:5080", Allowed));

    [Fact]
    public void SchemeAndHost_AreNormalizedToLowerCase() =>
        Assert.Equal(
            "https://portal.zynorahr.com",
            ForwardedOriginResolver.Resolve("HTTPS", "Portal.ZynoraHR.com", Allowed));

    /// <summary>مضيف منتحَل خارج القائمة البيضاء لا يُنتج أصلاً — يفشل تحقّق WebAuthn.</summary>
    [Fact]
    public void SpoofedHost_IsRejected() =>
        Assert.Null(ForwardedOriginResolver.Resolve("https", "evil.example.com", Allowed));

    [Theory]
    [InlineData("javascript", "portal.zynorahr.com")]
    [InlineData("ftp", "portal.zynorahr.com")]
    [InlineData("", "portal.zynorahr.com")]
    [InlineData("https", "")]
    [InlineData("https", "portal.zynorahr.com/evil")]
    [InlineData("https", "portal.zynorahr.com evil.com")]
    public void MalformedSchemeOrHost_IsRejected(string scheme, string host) =>
        Assert.Null(ForwardedOriginResolver.Resolve(scheme, host, Allowed));

    /// <summary>بلا قائمة بيضاء نعتمد المضيف المطبَّع (وضع LAN/التطوير).</summary>
    [Fact]
    public void WithoutAllowList_NormalizedHostIsAccepted() =>
        Assert.Equal(
            "http://192.168.1.20:5080",
            ForwardedOriginResolver.Resolve("http", "192.168.1.20:5080", null));

    [Fact]
    public void HostNameOnly_StripsPort() =>
        Assert.Equal("portal.zynorahr.com", ForwardedOriginResolver.HostNameOnly("Portal.ZynoraHR.com:443"));

    [Fact]
    public void AllowListMatches_HostWithoutPort() =>
        Assert.Equal(
            "https://portal.zynorahr.com:8443",
            ForwardedOriginResolver.Resolve("https", "portal.zynorahr.com:8443", Allowed));
}

public class SecurityHeaderPolicyTests
{
    private static readonly string Csp = SecurityHeaderPolicy.BuildContentSecurityPolicy();

    [Fact]
    public void DefaultSource_IsLockedToOwnOrigin() =>
        Assert.Contains("default-src 'self'", Csp);

    [Theory]
    [InlineData("object-src 'none'")]
    [InlineData("base-uri 'self'")]
    [InlineData("form-action 'self'")]
    [InlineData("frame-ancestors 'self'")]
    [InlineData("worker-src 'self'")]
    public void HardeningDirectives_ArePresent(string directive) =>
        Assert.Contains(directive, Csp);

    /// <summary>الخرائط المستعملة فعلاً يجب ألا تُكسر بالسياسة.</summary>
    [Theory]
    [InlineData("https://tile.openstreetmap.org")]
    [InlineData("https://server.arcgisonline.com")]
    public void MapTileHosts_AreAllowedForImages(string host)
    {
        var imgDirective = Csp.Split("; ").Single(d => d.StartsWith("img-src"));
        Assert.Contains(host, imgDirective);
    }

    [Fact]
    public void GeocodingHost_IsAllowedForXhrOnly()
    {
        var connectDirective = Csp.Split("; ").Single(d => d.StartsWith("connect-src"));
        Assert.Contains("https://nominatim.openstreetmap.org", connectDirective);
    }

    /// <summary>لا سكربتات من أي أصل خارجي — 'unsafe-inline' فقط للمضمّن المحلي.</summary>
    [Fact]
    public void ScriptSource_HasNoExternalHost()
    {
        var scriptDirective = Csp.Split("; ").Single(d => d.StartsWith("script-src"));
        Assert.DoesNotContain("https://", scriptDirective);
    }

    [Fact]
    public void WebAuthnAndGeolocation_StayEnabledForOwnOrigin()
    {
        var headers = SecurityHeaderPolicy.BuildStaticHeaders();

        Assert.Contains("geolocation=(self)", headers["Permissions-Policy"]);
        Assert.Contains("publickey-credentials-get=(self)", headers["Permissions-Policy"]);
        Assert.Contains("camera=()", headers["Permissions-Policy"]);
    }

    [Fact]
    public void ClickjackingAndSniffingHeaders_ArePreserved()
    {
        var headers = SecurityHeaderPolicy.BuildStaticHeaders();

        Assert.Equal("SAMEORIGIN", headers["X-Frame-Options"]);
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("strict-origin-when-cross-origin", headers["Referrer-Policy"]);
        Assert.True(headers.ContainsKey("Content-Security-Policy"));
    }
}
