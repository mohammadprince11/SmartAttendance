using System.Diagnostics;

namespace SmartAttendance.Web.Infrastructure.Observability;

/// <summary>
/// يقيس زمن كل طلبٍ ويسجّله في <see cref="RequestMetricsSink"/> (FIX-004 · OBS-006).
///
/// <para><b>الموضع بالخطّ:</b> باكراً جداً — قبل التوجيه والمصادقة — كي يلتقط
/// الطلب كاملاً بما فيه ردُّ 401/429. لكنه يقرأ قالب المسار <b>بعد</b> اكتماله
/// (من <c>Endpoint</c> المُطابَق في <c>finally</c>)، فيتوفّر عندئذٍ.</para>
///
/// <para><b>خصوصيّة:</b> يُسجَّل قالب المسار لا المسار الخام — فلا معرّفٌ ولا سلسلة
/// استعلامٍ يدخل. التكلفة زمنياً: <see cref="Stopwatch"/> ونداءٌ ذرّيّ — مهمَلة.</para>
/// </summary>
public sealed class RequestMetricsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RequestMetricsSink _sink;

    public RequestMetricsMiddleware(RequestDelegate next, RequestMetricsSink sink)
    {
        _next = next;
        _sink = sink;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            _sink.Record(
                ResolveRouteTemplate(context),
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// قالب المسار المُطابَق (مثل <c>/Employees/Edit</c> أو <c>files/download</c>)،
    /// أو الجزء الأول من المسار الخام حين لا يُطابَق شيء — بلا معرّفات.
    /// </summary>
    private static string ResolveRouteTemplate(HttpContext context)
    {
        var endpoint = context.GetEndpoint();

        if (endpoint is RouteEndpoint routeEndpoint)
        {
            return "/" + routeEndpoint.RoutePattern.RawText?.TrimStart('/');
        }

        if (endpoint is not null && !string.IsNullOrEmpty(endpoint.DisplayName))
        {
            return endpoint.DisplayName;
        }

        // بلا نقطة نهاية مُطابَقة (أصلٌ ساكن · 404): أوّل جزءٍ من المسار فقط —
        // يمنع تفجّر التعدّد ويُبقي الخصوصيّة (لا معرّف بعد الجزء الأول).
        var path = context.Request.Path.Value ?? "/";
        var firstSegmentEnd = path.IndexOf('/', 1);
        return firstSegmentEnd > 0 ? path[..firstSegmentEnd] : path;
    }
}
