using System.Collections.Concurrent;

namespace SmartAttendance.Web.Infrastructure.Observability;

/// <summary>
/// مجمّع مقاييس الطلبات بالذاكرة (FIX-004) — <b>طبقة تمكينٍ لا ميزة</b>.
///
/// <para><b>لماذا:</b> تدقيق الأداء (Phase 4 · OBS-006) أثبت أنه لا يمكن الإجابة
/// عن «ما الذي أبطأ النظام أمس؟» — لا زمن طلبٍ ولا P95 ولا معدّل أخطاء. وكل
/// تحسينٍ لاحق للأداء بلا خطّ أساسٍ لا يمكن إثباته (§102). هذه أدنى طبقةٍ تُنشئ
/// ذلك الخطّ: مسارٌ ← عدد ← أخطاء ← توزيع زمنٍ يعطي P50/P95/P99.</para>
///
/// <para><b>خصوصيّة (قاعدة حمراء — لا بيانات موظف بأي لوج):</b> يُجمَّع بـ<b>قالب
/// المسار</b> (<c>Route Pattern</c> مثل <c>/files/download</c>) لا بالمسار الخام،
/// فلا معرّفات ولا سلسلة استعلام تدخل الذاكرة. والمفاتيح محدودة بسقفٍ يمنع تضخّم
/// الرام. لا شيء يُكتب لقرصٍ أو لوج — ذاكرة العملية فقط، تزول بإعادة التشغيل.</para>
///
/// <para><b>Singleton مقصود:</b> الحالة مشتركة عبر كل الطلبات. آمنٌ للخيوط:
/// العدّادات <see cref="Interlocked"/> والقاموس متزامن. ⚠️ بنسختين تصير لكل
/// نسخةٍ نافذتها (SCALE-004) — مقبولٌ لأداة قياسٍ محليّة.</para>
/// </summary>
public sealed class RequestMetricsSink
{
    /// <summary>سقف المسارات المميّزة — يمنع تضخّم الذاكرة بمساراتٍ مُلفَّقة.</summary>
    public const int MaxDistinctRoutes = 1000;

    /// <summary>الحدود العليا لدلاء زمن الاستجابة بالملّي ثانية (تصاعديّة).</summary>
    internal static readonly int[] BucketUpperBoundsMs =
    {
        5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, int.MaxValue
    };

    private readonly ConcurrentDictionary<string, RouteMetric> _routes = new(StringComparer.Ordinal);
    private long _totalRequests;
    private long _totalServerErrors;
    private readonly DateTime _startedUtc = DateTime.UtcNow;

    public DateTime StartedUtc => _startedUtc;
    public long TotalRequests => Interlocked.Read(ref _totalRequests);
    public long TotalServerErrors => Interlocked.Read(ref _totalServerErrors);

    /// <summary>يسجّل طلباً منتهياً. المسار قالبٌ لا مسارٌ خام (خصوصيّة + حدُّ تعدّد).</summary>
    public void Record(string routeTemplate, int statusCode, long elapsedMs)
    {
        Interlocked.Increment(ref _totalRequests);
        if (statusCode >= 500)
        {
            Interlocked.Increment(ref _totalServerErrors);
        }

        var key = string.IsNullOrEmpty(routeTemplate) ? "(unmatched)" : routeTemplate;

        if (_routes.Count >= MaxDistinctRoutes && !_routes.ContainsKey(key))
        {
            key = "(overflow)";
        }

        var metric = _routes.GetOrAdd(key, k => new RouteMetric(k));
        metric.Add(statusCode, elapsedMs);
    }

    public IReadOnlyList<RouteSnapshot> Snapshot() =>
        _routes.Values
            .Select(r => r.ToSnapshot())
            .OrderByDescending(s => s.TotalMs)
            .ToList();

    public void Clear()
    {
        _routes.Clear();
        Interlocked.Exchange(ref _totalRequests, 0);
        Interlocked.Exchange(ref _totalServerErrors, 0);
    }

    /// <summary>حالة مسارٍ واحد — دلاءُ زمنٍ لا عيّناتٌ خام (ذاكرةٌ ثابتة لكل مسار).</summary>
    private sealed class RouteMetric
    {
        private readonly string _route;
        private readonly long[] _buckets = new long[BucketUpperBoundsMs.Length];
        private long _count;
        private long _errors;
        private long _totalMs;
        private long _maxMs;

        public RouteMetric(string route) => _route = route;

        public void Add(int statusCode, long elapsedMs)
        {
            Interlocked.Increment(ref _count);
            if (statusCode >= 400)
            {
                Interlocked.Increment(ref _errors);
            }
            Interlocked.Add(ref _totalMs, elapsedMs);

            // أقصى زمنٍ بحلقة CAS (بلا قفل).
            long observed;
            while (elapsedMs > (observed = Interlocked.Read(ref _maxMs)))
            {
                if (Interlocked.CompareExchange(ref _maxMs, elapsedMs, observed) == observed)
                {
                    break;
                }
            }

            var index = BucketIndex(elapsedMs);
            Interlocked.Increment(ref _buckets[index]);
        }

        public RouteSnapshot ToSnapshot()
        {
            var buckets = new long[_buckets.Length];
            for (var i = 0; i < _buckets.Length; i++)
            {
                buckets[i] = Interlocked.Read(ref _buckets[i]);
            }

            var count = Interlocked.Read(ref _count);

            return new RouteSnapshot(
                _route,
                count,
                Interlocked.Read(ref _errors),
                Interlocked.Read(ref _totalMs),
                Interlocked.Read(ref _maxMs),
                Percentile(buckets, count, 0.50),
                Percentile(buckets, count, 0.95),
                Percentile(buckets, count, 0.99));
        }

        private static int BucketIndex(long ms)
        {
            for (var i = 0; i < BucketUpperBoundsMs.Length; i++)
            {
                if (ms <= BucketUpperBoundsMs[i])
                {
                    return i;
                }
            }
            return BucketUpperBoundsMs.Length - 1;
        }

        /// <summary>
        /// تقدير المئينِ من الـhistogram: الدلو الذي يعبره العدُّ التراكميّ، ويُبلَّغ
        /// بحدّه الأعلى. تقريبٌ مقصود — دقّةٌ كافيةٌ للرصد لا للفوترة.
        /// </summary>
        private static long Percentile(long[] buckets, long count, double p)
        {
            if (count == 0)
            {
                return 0;
            }

            var target = (long)Math.Ceiling(p * count);
            long cumulative = 0;
            for (var i = 0; i < buckets.Length; i++)
            {
                cumulative += buckets[i];
                if (cumulative >= target)
                {
                    var bound = BucketUpperBoundsMs[i];
                    return bound == int.MaxValue ? 5000 : bound;
                }
            }
            return 0;
        }
    }

    public sealed record RouteSnapshot(
        string Route,
        long Count,
        long Errors,
        long TotalMs,
        long MaxMs,
        long P50Ms,
        long P95Ms,
        long P99Ms)
    {
        public double AvgMs => Count == 0 ? 0 : Math.Round((double)TotalMs / Count, 1);
        public double ErrorRate => Count == 0 ? 0 : Math.Round(100.0 * Errors / Count, 1);
    }
}
