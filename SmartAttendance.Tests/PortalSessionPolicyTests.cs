using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات حاجز إعارة الهاتف: مهلة الخمول (0=معطّل، حدّية)، إيقاع تجديد ختم
/// «آخر نشاط» بتذكرة الكوكي، وقراءة الإعدادات الآمنة (تالف/سالب/سقف).
/// </summary>
public class PortalSessionPolicyTests
{
    private static readonly DateTime Now = new(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);

    // ===== مهلة الخمول =====

    [Fact]
    public void Expire_ZeroMinutes_Disabled() =>
        Assert.False(PortalSessionPolicy.ShouldExpire(Now.AddHours(-10), Now, 0));

    [Fact]
    public void Expire_WithinWindow_No() =>
        Assert.False(PortalSessionPolicy.ShouldExpire(Now.AddMinutes(-29), Now, 30));

    [Fact]
    public void Expire_ExactBoundary_No() =>
        Assert.False(PortalSessionPolicy.ShouldExpire(Now.AddMinutes(-30), Now, 30));

    [Fact]
    public void Expire_PastWindow_Yes() =>
        Assert.True(PortalSessionPolicy.ShouldExpire(Now.AddMinutes(-31), Now, 30));

    // ===== تجديد الختم =====

    [Fact]
    public void Renew_FreshStamp_No() =>
        Assert.False(PortalSessionPolicy.ShouldRenew(Now.AddSeconds(-30), Now));

    [Fact]
    public void Renew_AfterInterval_Yes() =>
        Assert.True(PortalSessionPolicy.ShouldRenew(Now.AddSeconds(-61), Now));

    // ===== قراءة الإعدادات =====

    [Theory]
    [InlineData("30", 30)]      // قيمة سليمة
    [InlineData("0", 0)]        // صفر = معطّل (مقبول)
    [InlineData("-5", 99)]      // سالب ⟵ الافتراضي
    [InlineData("abc", 99)]     // تالف ⟵ الافتراضي
    [InlineData(null, 99)]      // مفقود ⟵ الافتراضي
    [InlineData("99999", 1440)] // فوق السقف ⟵ يُثبَّت عنده
    public void ParseSetting_Robust(string? raw, int expected) =>
        Assert.Equal(expected, PortalSessionPolicy.ParseSetting(raw, fallback: 99, max: 1440));

    [Fact]
    public void Defaults_MatchApprovedBehavior()
    {
        // الافتراضيات المطلوبة: خمول 30 دقيقة، مغادرة 60 ثانية — مفعّلة من أول نشرة.
        Assert.Equal(30, PortalSessionPolicy.DefaultIdleMinutes);
        Assert.Equal(60, PortalSessionPolicy.DefaultLeaveSeconds);
    }

    [Fact]
    public void RememberedSession_DoesNotUseIdleOrLeaveLogout()
    {
        Assert.False(PortalSessionPolicy.ShouldEnforceSessionTimeouts(isRememberedSession: true));
        Assert.True(PortalSessionPolicy.ShouldEnforceSessionTimeouts(isRememberedSession: false));
    }
}
