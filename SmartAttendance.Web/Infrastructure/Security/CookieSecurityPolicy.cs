namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>قرار راية <c>Secure</c> لكوكي الجلسة، وهل يُسمح بالإقلاع أصلاً.</summary>
public enum CookieSecurityDecision
{
    /// <summary>تُصدَر الكوكي بـ<c>Secure</c> دائماً.</summary>
    AlwaysSecure = 0,

    /// <summary>تتبع بروتوكول الطلب — مقبول خارج الإنتاج فقط.</summary>
    FollowRequest = 1,

    /// <summary>إنتاجٌ بلا TLS مؤكَّد ولا إذنٍ صريح ⟹ يُرفض الإقلاع.</summary>
    RefuseToStart = 2
}

/// <summary>
/// سياسة أمان كوكي الجلسة — نقيّة وقابلة للاختبار، خارج <c>Program.cs</c>.
///
/// <para><b>العطل المحروس:</b> كانت الراية مربوطة بـ<c>ForceHttps</c> وحدها
/// (افتراضها <c>false</c>). فبنشرٍ ينهي TLS عند الحافة ويمرّر HTTP داخلياً — وهو
/// النشر السحابيّ المعتاد — تُصدَر كوكي الجلسة <b>بلا <c>Secure</c></b>، فيرسلها
/// المتصفّح على أي اتصال غير مشفَّر لنفس النطاق.</para>
///
/// <para><b>مغلقة الفشل بالإنتاج، ومتساهلة خارجه:</b> الإنتاج يتطلّب إثباتاً على
/// وجود TLS — إمّا إجباره بالتطبيق (<c>ForceHttps</c>) أو الاعتراف بوسيطٍ عكسيّ
/// موثوق ينهيه (<c>ReverseProxy.Enabled</c>، وهو نفسه يفرض قائمة وسطاء صريحة).
/// وبلا أيٍّ منهما يُرفض الإقلاع بدل أن يعمل النظام بكوكي ضعيفة صامتاً.</para>
///
/// <para><b>مخرج صريح للنشر المحليّ:</b> شبكة داخلية على HTTP بلا شهادة حالة
/// مشروعة (وهي حالة هذا المستودع تاريخياً: بوت‑ستراب شهادة CA يحتاج HTTP). يفتحها
/// <c>Security:AllowInsecureCookies=true</c> — قرارٌ يُكتب بالإعدادات ويُقرأ
/// بالمراجعة، لا افتراضٌ صامت.</para>
/// </summary>
public static class CookieSecurityPolicy
{
    public const string AllowInsecureCookiesKey = "Security:AllowInsecureCookies";

    public static CookieSecurityDecision Evaluate(
        bool forceHttps,
        bool reverseProxyEnabled,
        bool isProduction,
        bool allowInsecureCookies)
    {
        // إجبار HTTPS بالتطبيق يحسم الأمر بأي بيئة.
        if (forceHttps) return CookieSecurityDecision.AlwaysSecure;

        if (!isProduction) return CookieSecurityDecision.FollowRequest;

        // إنتاج: وسيطٌ عكسيّ موثوق يعني أن TLS منتهٍ عند الحافة، وترويسة البروتوكول
        // المُمرَّرة منه موثوقة (`KnownProxies` صريحة) — فنُصدر Secure دائماً.
        if (reverseProxyEnabled) return CookieSecurityDecision.AlwaysSecure;

        // إنتاجٌ بلا إثبات TLS: يُسمح فقط بإذن صريح مكتوب.
        return allowInsecureCookies
            ? CookieSecurityDecision.FollowRequest
            : CookieSecurityDecision.RefuseToStart;
    }

    public static string BuildRefusalMessage() =>
        "رُفض الإقلاع: بيئة الإنتاج بلا إثباتٍ على TLS، فستُصدَر كوكي الجلسة بلا راية " +
        "Secure ويرسلها المتصفّح على اتصال غير مشفَّر. " +
        "اضبط ForceHttps=true (إجبار HTTPS بالتطبيق) أو ReverseProxy:Enabled=true " +
        "(وسيط عكسيّ موثوق ينهي TLS عند الحافة). " +
        $"وللنشر الداخليّ على HTTP عمداً اضبط {AllowInsecureCookiesKey}=true صراحةً.";
}
