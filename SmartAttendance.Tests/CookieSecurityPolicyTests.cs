using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// سياسة أمان كوكي الجلسة (P0-4 بتدقيق الجاهزية).
///
/// <para><b>العطل</b>: كانت الراية مربوطة بـ<c>ForceHttps</c> وحدها وافتراضها
/// <c>false</c>. فبنشرٍ ينهي TLS عند الحافة ويمرّر HTTP داخلياً — وهو النشر السحابيّ
/// المعتاد — تُصدَر كوكي الجلسة <b>بلا <c>Secure</c></b>، فيرسلها المتصفّح على أي
/// اتصال غير مشفَّر لنفس النطاق.</para>
///
/// <para>المطلوب من هذه السياسة شيئان متضادّان ظاهرياً: أن تُغلق الفشل بالإنتاج،
/// وألّا تكسر النشر الداخليّ على HTTP (وهو حالة مشروعة بهذا المستودع تاريخياً).
/// الاختبارات تحرس الطرفين معاً.</para>
/// </summary>
public class CookieSecurityPolicyTests
{
    // ═══ إجبار HTTPS يحسم بأي بيئة ═══

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void ForceHttps_AlwaysWins(bool reverseProxy, bool isProduction) =>
        Assert.Equal(
            CookieSecurityDecision.AlwaysSecure,
            CookieSecurityPolicy.Evaluate(
                forceHttps: true,
                reverseProxyEnabled: reverseProxy,
                isProduction: isProduction,
                allowInsecureCookies: false));

    // ═══ خارج الإنتاج: متساهلة ═══

    /// <summary>
    /// التطوير وLAN يحتاجان HTTP (بوت‑ستراب شهادة الـCA)؛ إغلاقهما يمنع العمل بلا
    /// مكسب أمنيّ — البيئة ليست إنتاجاً أصلاً.
    /// </summary>
    [Fact]
    public void NonProduction_FollowsTheRequest_WithoutRefusing() =>
        Assert.Equal(
            CookieSecurityDecision.FollowRequest,
            CookieSecurityPolicy.Evaluate(
                forceHttps: false,
                reverseProxyEnabled: false,
                isProduction: false,
                allowInsecureCookies: false));

    // ═══ الإنتاج: مغلقة الفشل ═══

    /// <summary>جوهر الإصلاح: إنتاجٌ بلا إثبات TLS لا يُقلع.</summary>
    [Fact]
    public void Production_WithoutAnyTlsProof_RefusesToStart() =>
        Assert.Equal(
            CookieSecurityDecision.RefuseToStart,
            CookieSecurityPolicy.Evaluate(
                forceHttps: false,
                reverseProxyEnabled: false,
                isProduction: true,
                allowInsecureCookies: false));

    /// <summary>
    /// الوسيط العكسيّ الموثوق إثباتٌ كافٍ: تفعيله نفسه يفرض قائمة وسطاء صريحة
    /// (`KnownProxies`) بـ`Program.cs`، فترويسة البروتوكول المُمرَّرة منه موثوقة.
    /// </summary>
    [Fact]
    public void Production_BehindTrustedReverseProxy_IsAlwaysSecure() =>
        Assert.Equal(
            CookieSecurityDecision.AlwaysSecure,
            CookieSecurityPolicy.Evaluate(
                forceHttps: false,
                reverseProxyEnabled: true,
                isProduction: true,
                allowInsecureCookies: false));

    /// <summary>
    /// المخرج الصريح للنشر الداخليّ. مقصودٌ أن يكون <b>إعداداً مكتوباً</b> يُقرأ
    /// بالمراجعة، لا افتراضاً صامتاً — الفرق بين قرارٍ واعٍ وسهوٍ.
    /// </summary>
    [Fact]
    public void Production_WithExplicitOptOut_IsAllowedButNotSecure() =>
        Assert.Equal(
            CookieSecurityDecision.FollowRequest,
            CookieSecurityPolicy.Evaluate(
                forceHttps: false,
                reverseProxyEnabled: false,
                isProduction: true,
                allowInsecureCookies: true));

    /// <summary>
    /// المخرج لا يُضعف حالةً كانت آمنة: مع إثبات TLS تبقى الكوكي Secure حتى لو
    /// كُتب المخرج بالإعدادات سهواً.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void OptOut_NeverDowngradesAProvenTlsDeployment(bool forceHttps, bool reverseProxy) =>
        Assert.Equal(
            CookieSecurityDecision.AlwaysSecure,
            CookieSecurityPolicy.Evaluate(
                forceHttps,
                reverseProxy,
                isProduction: true,
                allowInsecureCookies: true));

    [Fact]
    public void RefusalMessage_NamesEveryWayOut()
    {
        var message = CookieSecurityPolicy.BuildRefusalMessage();

        Assert.Contains("ForceHttps", message);
        Assert.Contains("ReverseProxy", message);
        Assert.Contains(CookieSecurityPolicy.AllowInsecureCookiesKey, message);
    }
}
