using System.Collections.Concurrent;
using System.Text.Json;

namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// مجمّع مخالفات CSP بالذاكرة — **أداة قياسٍ لترحيل #8، لا ميزة إنتاج**.
///
/// <para><b>لماذا:</b> السياسة الصارمة تُبَثّ «تقريراً فقط» منذ PR6 لكن بلا وجهة،
/// فالمخالفات تذهب لكونسول المتصفّح وتضيع. بلا جمعها يصير الترحيل تخميناً على 181
/// صفحة: نحوّل ما نظنّه ونقلب الإنفاذ فينكسر ما نسيناه. هنا نقيس أولاً.</para>
///
/// <para><b>خصوصيّة (قاعدة حمراء — لا بيانات موظف بأي لوج):</b> لا يُخزَّن
/// <c>script-sample</c> إطلاقاً، ولا سلسلة الاستعلام: المسار وحده + التوجيه +
/// المصدر المحجوب + رقم السطر. ولا شيء يُكتب لقرص أو لوج — ذاكرة الطلب فقط، تزول
/// بإعادة التشغيل. والمفاتيح محدودة بسقف فلا يُستنزف الرام بطلبات مُلفَّقة.</para>
/// </summary>
public sealed class CspViolationSink
{
    /// <summary>سقف المفاتيح المميّزة: الوجهة عامّة، فالسقف يمنع تضخّم الذاكرة.</summary>
    public const int MaxDistinctEntries = 2000;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public sealed record Entry(string DocumentPath, string Directive, string BlockedUri, int LineNumber)
    {
        public int Count;
    }

    /// <summary>
    /// يقرأ تقرير المتصفّح (<c>application/csp-report</c> أو Reporting API) ويسجّل
    /// مفتاحاً مجمَّعاً. أي شكلٍ غير متوقَّع يُتجاهَل بصمت: نقطةٌ عامّة لا تُخبر
    /// المرسِل بشيء، وأي استثناء هنا يعني رفض تقارير المتصفّح كلّها.
    /// </summary>
    public void Record(JsonElement report)
    {
        if (!report.TryGetProperty("csp-report", out var body))
        {
            body = report;
        }

        if (body.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var documentPath = PathOnly(ReadString(body, "document-uri", "documentURL"));
        var directive = Trim(ReadString(body, "effective-directive", "violated-directive", "effectiveDirective"), 60);
        var blocked = OriginOnly(ReadString(body, "blocked-uri", "blockedURL"));
        var line = ReadInt(body, "line-number", "lineNumber");

        if (documentPath.Length == 0 && directive.Length == 0)
        {
            return;
        }

        var key = $"{documentPath}|{directive}|{blocked}|{line}";

        if (_entries.Count >= MaxDistinctEntries && !_entries.ContainsKey(key))
        {
            return;
        }

        var entry = _entries.GetOrAdd(key, _ => new Entry(documentPath, directive, blocked, line));
        Interlocked.Increment(ref entry.Count);
    }

    /// <summary>الملخّص مرتّباً بالأكثر تكراراً — يقود ترتيب الترحيل صفحةً صفحة.</summary>
    public IReadOnlyList<Entry> Summary() =>
        _entries.Values.OrderByDescending(e => e.Count).ThenBy(e => e.DocumentPath, StringComparer.Ordinal).ToList();

    public void Clear() => _entries.Clear();

    private static string ReadString(JsonElement body, params string[] names)
    {
        foreach (var name in names)
        {
            if (body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static int ReadInt(JsonElement body, params string[] names)
    {
        foreach (var name in names)
        {
            if (body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var number))
            {
                return number;
            }
        }

        return 0;
    }

    /// <summary>المسار وحده: سلسلة الاستعلام قد تحمل معرّفات موظفين — تُقصّ دائماً.</summary>
    private static string PathOnly(string uri)
    {
        if (uri.Length == 0)
        {
            return string.Empty;
        }

        var path = Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            ? parsed.AbsolutePath
            : uri.Split('?', '#')[0];

        return Trim(path, 200);
    }

    /// <summary>«inline»/«eval» تبقى كما هي؛ العناوين تُختصر لأصلها بلا مسار.</summary>
    private static string OriginOnly(string blocked)
    {
        if (blocked.Length == 0)
        {
            return string.Empty;
        }

        if (Uri.TryCreate(blocked, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https")
        {
            return Trim(parsed.GetLeftPart(UriPartial.Authority), 120);
        }

        return Trim(blocked, 120);
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
