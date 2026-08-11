using System.Security.Cryptography;

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

    public static string BuildContentSecurityPolicy() => BuildContentSecurityPolicy(null);

    /// <summary>
    /// يبني CSP المُنفَّذة. <paramref name="nonce"/> فارغ ⟹ السياسة المتساهلة الحالية
    /// (<c>'unsafe-inline'</c>) — السلوك الحيّ الافتراضي. قيمةٌ ⟹ السياسة الصارمة:
    /// <c>'nonce-…'</c> بدل <c>'unsafe-inline'</c>، فلا يُشغّل المتصفّح إلا الوسوم
    /// المضمّنة الحاملة لهذا الـnonce (وتُعطَّل معالِجات الأحداث المضمّنة تلقائيّاً).
    /// تُفعَّل بالراية <c>Security:StrictCsp</c> بعد ترحيل الوسوم لبيئة يمكن اختبارها.
    /// </summary>
    public static string BuildContentSecurityPolicy(string? nonce)
    {
        var inline = string.IsNullOrEmpty(nonce) ? "'unsafe-inline'" : $"'nonce-{nonce}'";
        var directives = new List<string>
        {
            SharedDirectives[0],
            $"script-src 'self' {inline}",
            $"style-src 'self' {inline}"
        };
        directives.AddRange(SharedDirectives[1..]);

        return string.Join("; ", directives);
    }

    /// <summary>nonce عشوائيّ آمن تشفيريّاً لكل طلب (128 بت، Base64).</summary>
    public static string NewNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

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
    public static string BuildReportOnlyContentSecurityPolicy() =>
        BuildReportOnlyContentSecurityPolicy(null);

    /// <summary>
    /// <paramref name="reportUri"/> بقيمة ⟹ تُلحَق <c>report-uri</c>/<c>report-to</c>
    /// فتصل المخالفات لمجمّعٍ يُقاس به الترحيل بدل تخمينه. فارغة ⟹ الترويسة كما هي
    /// حرفيّاً (لا وجهة، لا نقطة نهاية بالإنتاج) — الافتراضي.
    /// </summary>
    public static string BuildReportOnlyContentSecurityPolicy(string? reportUri)
    {
        var directives = new List<string>
        {
            SharedDirectives[0],
            "script-src 'self'",
            "style-src 'self'"
        };
        directives.AddRange(SharedDirectives[1..]);

        if (!string.IsNullOrWhiteSpace(reportUri))
        {
            // الاثنان معاً: report-uri مهجورة بالمعيار لكنها المدعومة عمليّاً بأوسع
            // انتشار، وreport-to هي الخلف المعاصر — فلا تضيع تقارير متصفّحٍ ما.
            directives.Add($"report-uri {reportUri}");
            directives.Add("report-to csp-endpoint");
        }

        return string.Join("; ", directives);
    }

    /// <summary>الترويسات الثابتة المطبَّقة على كل استجابة.</summary>
    public static IReadOnlyDictionary<string, string> BuildStaticHeaders() =>
        BuildStaticHeaders(null);

    public static IReadOnlyDictionary<string, string> BuildStaticHeaders(string? cspReportUri)
    {
        var headers = new Dictionary<string, string>
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
            ["Content-Security-Policy-Report-Only"] = BuildReportOnlyContentSecurityPolicy(cspReportUri)
        };

        if (!string.IsNullOrWhiteSpace(cspReportUri))
        {
            headers["Reporting-Endpoints"] = $"csp-endpoint=\"{cspReportUri}\"";
        }

        return headers;
    }
}
