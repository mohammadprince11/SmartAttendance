using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Web.Infrastructure.Observability;

namespace SmartAttendance.Web.Pages.SystemPerformance;

/// <summary>
/// لوحة مقاييس الطلبات (FIX-004 · OBS-006) — <b>للأدمن فقط</b>.
///
/// <para>الحماية تلقائيّة: المسار <c>/systemperformance</c> غير مذكورٍ بأي كتالوج
/// دورٍ في <see cref="Infrastructure.Security.RoleRouteCatalog"/>، فـ
/// <see cref="Infrastructure.Security.RoleSecurityMiddleware"/> يمنع كل دورٍ عداه
/// (الأدمن وحده يعبر عبر <c>IsAdmin</c>).</para>
///
/// <para>تعرض ما جمعه <see cref="RequestMetricsSink"/> منذ آخر إقلاعٍ فقط — بلا
/// تخزين. الغرض خطُّ أساسٍ لإثبات تحسينات الأداء لاحقاً، لا مراقبةٌ دائمة.</para>
/// </summary>
public class IndexModel : PageModel
{
    private readonly RequestMetricsSink _sink;

    public IndexModel(RequestMetricsSink sink) => _sink = sink;

    public IReadOnlyList<RequestMetricsSink.RouteSnapshot> Routes { get; private set; } =
        Array.Empty<RequestMetricsSink.RouteSnapshot>();

    public long TotalRequests { get; private set; }
    public long TotalServerErrors { get; private set; }
    public DateTime StartedUtc { get; private set; }

    public double ServerErrorRate => TotalRequests == 0
        ? 0
        : Math.Round(100.0 * TotalServerErrors / TotalRequests, 2);

    public void OnGet()
    {
        Load();
    }

    public IActionResult OnPostReset()
    {
        _sink.Clear();
        return RedirectToPage();
    }

    private void Load()
    {
        Routes = _sink.Snapshot();
        TotalRequests = _sink.TotalRequests;
        TotalServerErrors = _sink.TotalServerErrors;
        StartedUtc = _sink.StartedUtc;
    }
}
