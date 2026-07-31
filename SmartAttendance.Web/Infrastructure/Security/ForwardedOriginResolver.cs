namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// إعدادات الوسيط العكسي (Cloudflare Tunnel محلياً). صريحة بالإعدادات لا مستنتجة:
/// بلا قائمة وسطاء موثوقين لا يُفعَّل احترام ترويسات X-Forwarded إطلاقاً.
/// </summary>
public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    /// <summary>تفعيل قراءة ترويسات X-Forwarded (لا تُقرأ إطلاقاً وهي false).</summary>
    public bool Enabled { get; set; }

    /// <summary>عناوين IP للوسطاء الموثوقين (cloudflared يعمل على نفس الجهاز عادة).</summary>
    public string[] KnownProxies { get; set; } = Array.Empty<string>();

    /// <summary>شبكات موثوقة بصيغة CIDR (مثال: 10.0.0.0/8).</summary>
    public string[] KnownNetworks { get; set; } = Array.Empty<string>();

    /// <summary>المضيفات المسموح أن تصل عبر الوسيط (قائمة بيضاء لـHost/Origin).</summary>
    public string[] AllowedHosts { get; set; } = Array.Empty<string>();
}

/// <summary>
/// المرحلة 10 — اشتقاق أصل (Origin) موثوق لتحقّق WebAuthn.
///
/// قبل: الكنترولر كان يقرأ <c>X-Forwarded-Proto</c> الخام من الطلب مباشرة — أي
/// عميل على الإنترنت يستطيع حقنها. الآن الترويسات يعالجها
/// <c>ForwardedHeadersMiddleware</c> لوسطاء موثوقين فقط، وهذه الدالة النقية تبني
/// الأصل من القيم <b>المطبَّعة</b> وتفرض قائمة مضيفات بيضاء عند وجودها.
/// </summary>
public static class ForwardedOriginResolver
{
    /// <summary>يرجع الأصل الموثوق، أو null إن كان المضيف خارج القائمة البيضاء.</summary>
    public static string? Resolve(
        string? scheme,
        string? host,
        IReadOnlyCollection<string>? allowedHosts)
    {
        if (string.IsNullOrWhiteSpace(scheme) || string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var normalizedScheme = scheme.Trim().ToLowerInvariant();

        if (normalizedScheme is not ("http" or "https"))
        {
            return null;
        }

        var normalizedHost = host.Trim().ToLowerInvariant();

        if (normalizedHost.Length == 0 || normalizedHost.Contains('/') || normalizedHost.Contains(' '))
        {
            return null;
        }

        if (allowedHosts is { Count: > 0 } && !IsAllowedHost(normalizedHost, allowedHosts))
        {
            return null;
        }

        return $"{normalizedScheme}://{normalizedHost}";
    }

    /// <summary>المضيف بلا المنفذ — يستعمله Fido2 كـServerDomain (RP ID).</summary>
    public static string HostNameOnly(string? host)
    {
        var value = (host ?? string.Empty).Trim().ToLowerInvariant();
        var separator = value.LastIndexOf(':');

        return separator > 0 ? value[..separator] : value;
    }

    private static bool IsAllowedHost(
        string normalizedHost,
        IReadOnlyCollection<string> allowedHosts)
    {
        var hostOnly = HostNameOnly(normalizedHost);

        foreach (var allowed in allowedHosts)
        {
            if (string.IsNullOrWhiteSpace(allowed))
            {
                continue;
            }

            var candidate = allowed.Trim().ToLowerInvariant();

            if (candidate == "*")
            {
                return true;
            }

            if (candidate == normalizedHost || candidate == hostOnly)
            {
                return true;
            }
        }

        return false;
    }
}
