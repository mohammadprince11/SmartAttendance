using SmartAttendance.Web.Infrastructure.Observability;

namespace SmartAttendance.Tests;

/// <summary>
/// FIX-004 — مقاييس الطلبات. عيبٌ هنا = خطُّ أساسٍ كاذب يُفسد كل قرار أداءٍ يُبنى
/// عليه (Phase 4 · OBS-006). القيم المتوقَّعة محسوبةٌ يدوياً بالتعليق.
/// </summary>
public class RequestMetricsSinkTests
{
    [Fact]
    public void Percentiles_FromKnownDistribution_LandInExpectedBuckets()
    {
        var sink = new RequestMetricsSink();

        // 100 طلبٍ متدرّج: 90 منها ≤ 100ms، و10 ثقيلة ~2000ms.
        for (var i = 0; i < 90; i++) sink.Record("/x", 200, 40);   // دلو ≤50
        for (var i = 0; i < 10; i++) sink.Record("/x", 200, 2000); // دلو ≤2500

        var s = sink.Snapshot().Single();

        Assert.Equal(100, s.Count);
        // P50: target=ceil(0.50×100)=50 ≤ تراكم الخفيفة (90) ⟹ دلو ≤50
        Assert.Equal(50, s.P50Ms);
        // P95: target=95 > 90 ⟹ يعبر للذيل الثقيل (الذيل 10% فالمئين 95 داخله) ⟹ دلو ≤2500
        Assert.Equal(2500, s.P95Ms);
        // P99: target=99 > 90 ⟹ الذيل الثقيل أيضاً
        Assert.Equal(2500, s.P99Ms);
    }

    [Fact]
    public void MaxAndAverage_AreExact()
    {
        var sink = new RequestMetricsSink();
        sink.Record("/y", 200, 10);
        sink.Record("/y", 200, 20);
        sink.Record("/y", 200, 60);

        var s = sink.Snapshot().Single();

        Assert.Equal(60, s.MaxMs);
        Assert.Equal(30.0, s.AvgMs); // (10+20+60)/3
    }

    [Fact]
    public void ErrorRate_CountsFourXxAndFiveXx_PerRoute()
    {
        var sink = new RequestMetricsSink();
        sink.Record("/z", 200, 5);
        sink.Record("/z", 404, 5);
        sink.Record("/z", 500, 5);
        sink.Record("/z", 200, 5);

        var s = sink.Snapshot().Single();

        Assert.Equal(2, s.Errors);       // 404 + 500
        Assert.Equal(50.0, s.ErrorRate); // 2/4
    }

    [Fact]
    public void ServerErrorRate_CountsOnlyFiveXx_Globally()
    {
        var sink = new RequestMetricsSink();
        sink.Record("/a", 200, 5);
        sink.Record("/a", 404, 5); // ليست خطأ خادم
        sink.Record("/b", 503, 5); // خطأ خادم

        Assert.Equal(3, sink.TotalRequests);
        Assert.Equal(1, sink.TotalServerErrors);
    }

    [Fact]
    public void DistinctRouteCap_RedirectsOverflowToSingleBucket()
    {
        var sink = new RequestMetricsSink();

        // أكثر من السقف بكثير — يجب ألّا تتضخّم الذاكرة بلا حدّ.
        for (var i = 0; i < RequestMetricsSink.MaxDistinctRoutes + 500; i++)
        {
            sink.Record($"/route-{i}", 200, 5);
        }

        var routes = sink.Snapshot();

        Assert.True(routes.Count <= RequestMetricsSink.MaxDistinctRoutes + 1); // +1 للفائض
        Assert.Contains(routes, r => r.Route == "(overflow)");
        // ولا يضيع العدّ الإجماليّ رغم الحدّ.
        Assert.Equal(RequestMetricsSink.MaxDistinctRoutes + 500, sink.TotalRequests);
    }

    [Fact]
    public void EmptyRoute_IsRecordedAsUnmatched_NotDropped()
    {
        var sink = new RequestMetricsSink();
        sink.Record("", 200, 5);

        Assert.Equal("(unmatched)", sink.Snapshot().Single().Route);
    }

    [Fact]
    public void Clear_ResetsEverything()
    {
        var sink = new RequestMetricsSink();
        sink.Record("/x", 500, 5);
        sink.Clear();

        Assert.Empty(sink.Snapshot());
        Assert.Equal(0, sink.TotalRequests);
        Assert.Equal(0, sink.TotalServerErrors);
    }

    [Fact]
    public void ConcurrentRecording_IsThreadSafe_AndLosesNoCount()
    {
        var sink = new RequestMetricsSink();

        Parallel.For(0, 10_000, i => sink.Record("/hot", i % 7 == 0 ? 500 : 200, i % 100));

        Assert.Equal(10_000, sink.TotalRequests);
        Assert.Equal(10_000, sink.Snapshot().Single().Count);
    }
}
