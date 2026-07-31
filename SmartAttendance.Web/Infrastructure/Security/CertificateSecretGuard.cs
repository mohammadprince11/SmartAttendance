namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>نتيجة فحص إعداد شهادة Kestrel قبل الإقلاع.</summary>
public enum CertificateSecretState
{
    /// <summary>الشهادة غير مستعملة بهذا التشغيل (لا نقطة HTTPS أو لا ملف) — لا شأن لنا.</summary>
    NotUsed = 0,

    /// <summary>الإعداد مكتمل.</summary>
    Ok = 1,

    /// <summary>الشهادة مطلوبة لكن كلمة مرورها غير مضبوطة بالبيئة ⟹ فشل واضح.</summary>
    MissingPassword = 2
}

/// <summary>
/// كلمة مرور شهادة LAN كانت **نصاً صريحاً بـappsettings.json** داخل المستودع.
/// أُزيلت، ومصدرها الآن متغيّر البيئة
/// <c>Kestrel__Certificates__Default__Password</c> (يقرأه مزوّد الإعدادات تلقائياً).
///
/// الخطر بعد الإزالة أن يقلع النظام ويفشل ربط HTTPS برسالة غامضة من Kestrel، أو
/// أسوأ: أن يمرّ الأمر صامتاً. لذا هذه الدالة النقية تقرّر بوضوح: الشهادة مطلوبة
/// (نقطة HTTPS + ملف موجود) وكلمة المرور غائبة ⟹ نفشل بالإقلاع برسالة تقول
/// بالضبط أي متغيّر يُضبط. وإن كان التشغيل HTTP فقط (LAN/خلف النفق) فلا اعتراض.
/// </summary>
public static class CertificateSecretGuard
{
    public const string PasswordEnvironmentVariable =
        "Kestrel__Certificates__Default__Password";

    public static CertificateSecretState Evaluate(
        bool httpsEndpointConfigured,
        bool certificateFileExists,
        string? password)
    {
        if (!httpsEndpointConfigured || !certificateFileExists)
        {
            return CertificateSecretState.NotUsed;
        }

        return string.IsNullOrWhiteSpace(password)
            ? CertificateSecretState.MissingPassword
            : CertificateSecretState.Ok;
    }

    /// <summary>هل تحوي عناوين التشغيل نقطة HTTPS؟ (urls أو نقاط Kestrel).</summary>
    public static bool HasHttpsEndpoint(IEnumerable<string?> configuredUrls) =>
        configuredUrls.Any(url =>
            !string.IsNullOrWhiteSpace(url) &&
            url.Contains("https://", StringComparison.OrdinalIgnoreCase));

    public static string BuildFailureMessage(string certificatePath) =>
        $"شهادة HTTPS '{certificatePath}' موجودة ونقطة HTTPS مُعرَّفة، لكن كلمة مرور " +
        $"الشهادة غير مضبوطة. اضبط متغيّر البيئة '{PasswordEnvironmentVariable}' " +
        "(لا تُعِد كتابتها داخل appsettings — الملف بالمستودع).";
}
