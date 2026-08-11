using System.Text.Json;

namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// نقطتا مجمّع مخالفات CSP — تُخريطان **فقط** حين ترفع الراية
/// <c>Security:CspReportCollector</c> (مطفأة افتراضيّاً)، والملخّص يقتصر فوق ذلك
/// على بيئة التطوير. بالإنتاج لا يوجد المساران أصلاً (404).
/// </summary>
public static class CspReportEndpoint
{
    public const string ReportPath = "/csp-report";
    public const string SummaryPath = "/csp-report/summary";

    /// <summary>حدّ حجم الجسم: تقارير المتصفّح صغيرة، والزائد جسمٌ مُلفَّق.</summary>
    private const int MaxBodyBytes = 16 * 1024;

    private static readonly CspViolationSink Sink = new();

    public static void MapCspReportCollector(this WebApplication app)
    {
        // الوجهة عامّة بالضرورة: المتصفّح يرسل التقرير من صفحة الدخول ومن جلسةٍ
        // منتهية أيضاً. لا تقرأ شيئاً ولا تردّ محتوى — 204 دائماً.
        app.MapPost(ReportPath, async (HttpContext context) =>
        {
            if (context.Request.ContentLength > MaxBodyBytes)
            {
                return Results.NoContent();
            }

            try
            {
                using var reader = new StreamReader(context.Request.Body);
                var raw = await reader.ReadToEndAsync(context.RequestAborted);
                if (raw.Length is > 0 and <= MaxBodyBytes)
                {
                    using var document = JsonDocument.Parse(raw);
                    Sink.Record(document.RootElement);
                }
            }
            catch (JsonException)
            {
                // جسمٌ غير صالح: يُتجاهَل بصمت — لا نُخبر مرسِلاً مجهولاً بشيء.
            }

            return Results.NoContent();
        }).AllowAnonymous();

        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        app.MapGet(SummaryPath, () => Results.Json(new
        {
            entries = Sink.Summary().Select(entry => new
            {
                path = entry.DocumentPath,
                directive = entry.Directive,
                blocked = entry.BlockedUri,
                line = entry.LineNumber,
                count = entry.Count
            })
        })).AllowAnonymous();
    }
}
