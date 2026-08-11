using System.Text.Json;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// مجمّع مخالفات CSP — الخطوة صفر لترحيل #8: **نقيس قبل أن نرحّل**. 181 صفحة و489
/// معالِج حدث مضمّن لا تُرحَّل بالتخمين؛ السياسة الصارمة مبثوثة «تقريراً فقط» منذ
/// PR6 لكن بلا وجهة، فالمخالفات كانت تضيع بكونسول المتصفّح.
///
/// <para>ويحرس هذا الملف القاعدة الحمراء: **لا بيانات موظف بأي لوج** — لا
/// <c>script-sample</c> ولا سلسلة استعلام تدخل المجمّع أبداً.</para>
/// </summary>
public sealed class CspViolationSinkTests
{
    [Fact]
    public void QueryString_IsStripped_SoEmployeeIdsNeverEnterTheSink()
    {
        var sink = new CspViolationSink();
        sink.Record(Report("""
{"csp-report":{"document-uri":"https://portal.zynorahr.com/Employees/Profile?id=4821&no=E-119",
"effective-directive":"script-src","blocked-uri":"inline","line-number":42}}
"""));

        var entry = Assert.Single(sink.Summary());
        Assert.Equal("/Employees/Profile", entry.DocumentPath);
        Assert.DoesNotContain("4821", entry.DocumentPath);
    }

    [Fact]
    public void ScriptSample_IsNeverStored()
    {
        var sink = new CspViolationSink();
        sink.Record(Report("""
{"csp-report":{"document-uri":"/Payroll/Runs","effective-directive":"script-src",
"blocked-uri":"inline","script-sample":"showEmployee('محمد', 4821)"}}
"""));

        var serialized = JsonSerializer.Serialize(sink.Summary());
        Assert.DoesNotContain("4821", serialized);
        Assert.DoesNotContain("script-sample", serialized);
    }

    [Fact]
    public void IdenticalViolations_Aggregate_InsteadOfPilingUp()
    {
        var sink = new CspViolationSink();
        for (var i = 0; i < 5; i++)
        {
            sink.Record(Report("""
{"csp-report":{"document-uri":"/Payroll/Runs","effective-directive":"script-src","blocked-uri":"inline","line-number":7}}
"""));
        }

        var entry = Assert.Single(sink.Summary());
        Assert.Equal(5, entry.Count);
    }

    [Fact]
    public void ReportingApiShape_IsAlsoUnderstood()
    {
        // report-to يرسل الحقول بأسماء camelCase وبلا غلاف csp-report.
        var sink = new CspViolationSink();
        sink.Record(Report("""
{"documentURL":"/Roster","effectiveDirective":"style-src","blockedURL":"inline","lineNumber":3}
"""));

        Assert.Equal("/Roster", Assert.Single(sink.Summary()).DocumentPath);
    }

    [Fact]
    public void UnknownShape_IsIgnored_NotThrown()
    {
        var sink = new CspViolationSink();
        sink.Record(Report("""{"hello":"world"}"""));
        Assert.Empty(sink.Summary());
    }

    [Fact]
    public void ReportUri_IsAbsentByDefault_SoProductionHeadersAreUnchanged()
    {
        var headers = SecurityHeaderPolicy.BuildStaticHeaders();
        Assert.DoesNotContain("report-uri", headers["Content-Security-Policy-Report-Only"]);
        Assert.False(headers.ContainsKey("Reporting-Endpoints"));
    }

    [Fact]
    public void ReportUri_IsAdded_OnlyWhenTheCollectorIsOn()
    {
        var headers = SecurityHeaderPolicy.BuildStaticHeaders(CspReportEndpoint.ReportPath);
        var reportOnly = headers["Content-Security-Policy-Report-Only"];

        Assert.Contains("report-uri /csp-report", reportOnly);
        Assert.Contains("report-to csp-endpoint", reportOnly);
        Assert.Equal("csp-endpoint=\"/csp-report\"", headers["Reporting-Endpoints"]);

        // المُنفَّذة لا تتغيّر: القياس لا يحجب شيئاً.
        Assert.Contains("'unsafe-inline'", headers["Content-Security-Policy"]);
    }

    [Fact]
    public void CollectorPaths_AreClassifiedPublic_ForUnauthenticatedBrowserReports()
    {
        Assert.Equal(PathAccessClass.Public, PublicPathPolicy.Classify("/csp-report"));
        Assert.Equal(PathAccessClass.Public, PublicPathPolicy.Classify("/csp-report/summary"));
    }

    private static JsonElement Report(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
