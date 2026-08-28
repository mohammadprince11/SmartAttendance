using SmartAttendance.Web.Infrastructure.Notifications;

namespace SmartAttendance.Web.Infrastructure.Security;

public sealed class ProductionOperationsOptions
{
    public const string SectionName = "Operations";

    public bool EnforceProductionReadiness { get; set; } = true;
    public string OwnerAcceptanceReference { get; set; } = string.Empty;
    public int RpoMinutes { get; set; }
    public int RtoMinutes { get; set; }
    public string OffsiteBackupPath { get; set; } = string.Empty;
    public string BackupHeartbeatPath { get; set; } = string.Empty;
    public string HealthMonitorUrl { get; set; } = string.Empty;
    public string AlertWebhookUrl { get; set; } = string.Empty;
}

/// <summary>
/// بوابة إقلاع إنتاجية: تحوّل البنود التشغيلية من ملاحظات وثيقة إلى شروط قابلة
/// للتدقيق. لا تفترض أن كتابة قيمة تعني نجاح مهمة خارجية؛ نبضة النسخ والمراقب
/// المستقلان يقدّمان الدليل التشغيلي بعد التثبيت.
/// </summary>
public static class ProductionReadinessGuard
{
    public static IReadOnlyList<string> Validate(
        string? environmentName,
        ProductionOperationsOptions operations,
        SmtpOptions smtp,
        MalwareScanningOptions malware)
    {
        if (!string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase)
            || !operations.EnforceProductionReadiness)
        {
            return Array.Empty<string>();
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(operations.OwnerAcceptanceReference))
            failures.Add("Operations:OwnerAcceptanceReference مفقود.");
        if (operations.RpoMinutes <= 0)
            failures.Add("Operations:RpoMinutes يجب أن يكون أكبر من صفر.");
        if (operations.RtoMinutes <= 0)
            failures.Add("Operations:RtoMinutes يجب أن يكون أكبر من صفر.");
        if (string.IsNullOrWhiteSpace(operations.OffsiteBackupPath))
            failures.Add("Operations:OffsiteBackupPath مفقود.");
        if (string.IsNullOrWhiteSpace(operations.BackupHeartbeatPath))
            failures.Add("Operations:BackupHeartbeatPath مفقود.");
        if (!Uri.TryCreate(operations.HealthMonitorUrl, UriKind.Absolute, out var healthUri)
            || healthUri.Scheme is not ("http" or "https"))
            failures.Add("Operations:HealthMonitorUrl يجب أن يكون رابط HTTP/HTTPS مطلقاً.");
        if (!Uri.TryCreate(operations.AlertWebhookUrl, UriKind.Absolute, out var alertUri)
            || alertUri.Scheme != Uri.UriSchemeHttps)
            failures.Add("Operations:AlertWebhookUrl يجب أن يكون رابط HTTPS مطلقاً.");
        if (!smtp.IsUsable)
            failures.Add("قناة SMTP غير مكتملة؛ لا يُسمح بمرسِل No-Op في الإنتاج.");
        if (!malware.Required || !malware.IsUsable)
            failures.Add("فحص malware يجب أن يكون Required ومتصلاً بمحرك صالح في الإنتاج.");

        return failures;
    }

    public static string BuildFailureMessage(IEnumerable<string> failures) =>
        "رُفض إقلاع ZYNORA بالإنتاج لأن بوابة التشغيل غير مكتملة:\n- "
        + string.Join("\n- ", failures);
}
