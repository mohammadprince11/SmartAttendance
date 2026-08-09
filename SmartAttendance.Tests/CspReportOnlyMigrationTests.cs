using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// PR6 #8 — بدء ترحيل CSP: بثّ السياسة الصارمة كـ«تقرير فقط» دون تغيير الإنفاذ.
/// الاختبارات تثبّت أنّ الإنفاذ يبقى كما هو (صفر خطر واجهة) وأنّ الهدف الصارم فعّال.
/// </summary>
public sealed class CspReportOnlyMigrationTests
{
    [Fact]
    public void EnforcedPolicy_StillAllowsInlineForNow()
    {
        var csp = SecurityHeaderPolicy.BuildContentSecurityPolicy();
        Assert.Contains("script-src 'self' 'unsafe-inline'", csp);
        Assert.Contains("style-src 'self' 'unsafe-inline'", csp);
    }

    [Fact]
    public void ReportOnlyPolicy_DropsUnsafeInline()
    {
        var csp = SecurityHeaderPolicy.BuildReportOnlyContentSecurityPolicy();
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("style-src 'self'", csp);
        Assert.DoesNotContain("'unsafe-inline'", csp);
    }

    [Fact]
    public void BothPolicies_ShareHardenedDirectives()
    {
        var enforced = SecurityHeaderPolicy.BuildContentSecurityPolicy();
        var report = SecurityHeaderPolicy.BuildReportOnlyContentSecurityPolicy();

        foreach (var directive in new[]
        {
            "default-src 'self'", "object-src 'none'", "base-uri 'self'",
            "form-action 'self'", "frame-ancestors 'self'"
        })
        {
            Assert.Contains(directive, enforced);
            Assert.Contains(directive, report);
        }
    }

    [Fact]
    public void StaticHeaders_EmitBothEnforcedAndReportOnly()
    {
        var headers = SecurityHeaderPolicy.BuildStaticHeaders();

        Assert.True(headers.ContainsKey("Content-Security-Policy"));
        Assert.True(headers.ContainsKey("Content-Security-Policy-Report-Only"));
        // الإنفاذ يبقى متساهلاً؛ التقرير فقط هو الصارم.
        Assert.Contains("'unsafe-inline'", headers["Content-Security-Policy"]);
        Assert.DoesNotContain("'unsafe-inline'", headers["Content-Security-Policy-Report-Only"]);
    }
}
