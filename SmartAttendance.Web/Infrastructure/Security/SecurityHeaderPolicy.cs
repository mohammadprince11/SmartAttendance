namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// المرحلة 10 — ترويسات الأمان وسياسة المحتوى (CSP).
///
/// السياسة عملية لا مثالية: الحل يستعمل Razor Pages بسكربتات وأنماط مضمّنة
/// (inline) بمئات الصفحات، فإسقاط <c>'unsafe-inline'</c> اليوم يكسر النظام كله.
/// المكسب الفعلي هنا: قفل <c>default-src</c> على أصلنا، ومنع الأصول الخارجية
/// عدا خرائط Leaflet/OSM/Esri المستعملة فعلاً، ومنع الـobject/base/form-action،
/// وحصر التأطير بأصلنا. إزالة <c>'unsafe-inline'</c> تحتاج ترحيل nonce لكل
/// الصفحات — مسجَّلة كعمل لاحق بمستند التدقيق.
/// </summary>
public static class SecurityHeaderPolicy
{
    /// <summary>مضيفات الخرائط المستعملة فعلاً بالنظام (بصمة الموقع/الجيوفنس).</summary>
    private const string MapTileHosts =
        "https://tile.openstreetmap.org https://*.tile.openstreetmap.org " +
        "https://server.arcgisonline.com";

    private const string MapApiHosts = "https://nominatim.openstreetmap.org";

    /// <summary>
    /// التوجيهات المشتركة بين السياسة المُنفَّذة و«التقرير فقط» — كل ما عدا
    /// <c>script-src</c>/<c>style-src</c> اللذين يفترقان في <c>'unsafe-inline'</c>.
    /// </summary>
    private static readonly string[] SharedDirectives =
    {
        "default-src 'self'",
        // data:/blob: لمعاينة الصور المرفوعة وقراءات الكانفس؛ الخرائط من مضيفيها.
        $"img-src 'self' data: blob: {MapTileHosts}",
        "font-src 'self' data:",
        $"connect-src 'self' {MapApiHosts}",
        // WebAuthn وGeolocation لا يحتاجان أصولاً خارجية — نمنع كل ما عداها.
        "object-src 'none'",
        "base-uri 'self'",
        "form-action 'self'",
        "frame-ancestors 'self'",
        // عامل خدمة الـPWA يُسجَّل من أصلنا فقط.
        "worker-src 'self'",
        "manifest-src 'self'"
    };

    public static string BuildContentSecurityPolicy()
    {
        var directives = new List<string>
        {
            SharedDirectives[0],
            // Razor + جافاسكربت محلي؛ 'unsafe-inline' مطلوب للسكربتات المضمّنة الحالية.
            "script-src 'self' 'unsafe-inline'",
            "style-src 'self' 'unsafe-inline'"
        };
        directives.AddRange(SharedDirectives[1..]);

        return string.Join("; ", directives);
    }

    /// <summary>
    /// السياسة الصارمة الهدف (بلا <c>'unsafe-inline'</c>) تُبَثّ كـ
    /// <c>Content-Security-Policy-Report-Only</c> — أوّل خطوة ترحيل #8 (PR6).
    ///
    /// <para><b>لا تحجب شيئاً بالتصميم</b>: ترويسة «التقرير فقط» تُبلّغ المتصفّح
    /// بالمخالفات دون منعها، فبثّها آمنٌ على الواجهة الحيّة كاملاً — بينما تكشف بدقّة
    /// السكربتات/الأنماط/المعالِجات المضمّنة التي يجب ترحيلها قبل قلب الإنفاذ. تبقى
    /// السياسة المُنفَّذة أعلاه كما هي حرفيّاً حتى يكتمل الترحيل (nonce + refactor
    /// معالِجات الأحداث المضمّنة).</para>
    /// </summary>
    public static string BuildReportOnlyContentSecurityPolicy()
    {
        var directives = new List<string>
        {
            SharedDirectives[0],
            "script-src 'self'",
            "style-src 'self'"
        };
        directives.AddRange(SharedDirectives[1..]);

        return string.Join("; ", directives);
    }

    /// <summary>الترويسات الثابتة المطبَّقة على كل استجابة.</summary>
    public static IReadOnlyDictionary<string, string> BuildStaticHeaders() =>
        new Dictionary<string, string>
        {
            ["X-Content-Type-Options"] = "nosniff",
            // SAMEORIGIN لا DENY: تسمح لبوابة الموظف بتضمين صفحات طلباتها بمكانها
            // (iframe) مع منع أي موقع خارجي من تأطيرنا (حماية من clickjacking).
            ["X-Frame-Options"] = "SAMEORIGIN",
            ["Referrer-Policy"] = "strict-origin-when-cross-origin",
            // geolocation=(self): يسمح لبصمة الموقع (geofence) من نطاقنا فقط،
            // publickey-credentials-get=(self) يلزم WebAuthn داخل نطاقنا.
            ["Permissions-Policy"] =
                "camera=(), microphone=(), geolocation=(self), publickey-credentials-get=(self)",
            ["Content-Security-Policy"] = BuildContentSecurityPolicy(),
            // بثّ الهدف الصارم بلا إنفاذ — يكشف سطح الترحيل بلا كسر الواجهة (#8).
            ["Content-Security-Policy-Report-Only"] = BuildReportOnlyContentSecurityPolicy()
        };
}
