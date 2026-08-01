using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات محرّك رموز الدمج ومنقّح HTML.
///
/// **أخطر اختبارين هنا أمنيان**:
/// <see cref="TokenValues_AreHtmlEscaped"/> — قيمة تحمل وسماً تُدرَج بوثيقة HTML،
/// وإدراجها خاماً يحقن سكربتاً بكل وثيقة تُولَّد لذلك الموظف.
/// <see cref="Sanitizer_StripsScriptAndEventHandlers"/> — القالب يكتبه HR ويُعرض
/// للموظفين، فقالبٌ واحد ملوَّث يسرق جلسة كل من يفتح وثيقةً منه.
/// </summary>
public class DocumentTokenEngineTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 1);

    private static HrConditionFacts.EmployeeRow Row() => new()
    {
        Id = 7,
        EmployeeNo = "E-007",
        BranchId = 2,
        DepartmentId = 3,
        Gender = "ذكر",
        Nationality = "عراقي",
        IsCitizen = true,
        IsActive = true,
        HireDate = new DateOnly(2024, 2, 1),
        BasicSalary = 1_200_000m,
        Allowances = 300_000m
    };

    private static DocumentTokenEngine.EmployeeExtras Extras(string fullName = "محمد علي") =>
        new(fullName, "Mohammed Ali", "محمد", "علي", "199012345", "A1234567",
            "07700000000", "m@example.com", "مهندس", "الهندسة", "المركز",
            "سامي", "C-100", new DateOnly(2024, 2, 1), "مصرف الرافدين");

    private static DocumentTokenEngine.DocumentContext Context() =>
        new("DOC26-1", AsOf, "hr.user", "زينورا", "المركز", "IQD");

    private static Dictionary<string, string> Tokens(string fullName = "محمد علي") =>
        DocumentTokenEngine.Build(Row(), Extras(fullName), Context(), AsOf);

    // ── الأمن ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TokenValues_AreHtmlEscaped()
    {
        var tokens = Tokens("<script>alert(1)</script>");
        var result = DocumentTokenEngine.Render("<p>{Employee-FullName}</p>", tokens);

        Assert.DoesNotContain("<script>", result.Html);
        Assert.Contains("&lt;script&gt;", result.Html);
    }

    [Fact]
    public void Sanitizer_StripsScriptAndEventHandlers()
    {
        var dirty = "<p onclick=\"steal()\">مرحباً</p><script>fetch('/x')</script><img src=x onerror=alert(1)>";
        var clean = DocumentHtmlSanitizer.Sanitize(dirty);

        Assert.DoesNotContain("script", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", clean, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("مرحباً", clean);
    }

    [Fact]
    public void Sanitizer_DropsScriptContentNotJustItsTag()
    {
        // إبقاء نصّ السكربت بعد إسقاط وسمه يترك كوداً معروضاً بالوثيقة.
        var clean = DocumentHtmlSanitizer.Sanitize("<div>قبل<script>var secret=1;</script>بعد</div>");

        Assert.DoesNotContain("secret", clean);
        Assert.Contains("قبل", clean);
        Assert.Contains("بعد", clean);
    }

    [Fact]
    public void Sanitizer_BlocksJavascriptSchemeInStyle()
    {
        var clean = DocumentHtmlSanitizer.Sanitize("<p style=\"background-color: url(javascript:alert(1))\">س</p>");

        Assert.DoesNotContain("javascript", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url(", clean, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitizer_KeepsAllowedFormatting()
    {
        var clean = DocumentHtmlSanitizer.Sanitize(
            "<p style=\"text-align: center; color: #333\"><b>عنوان</b></p><table><tr><td colspan=\"2\">خلية</td></tr></table>");

        Assert.Contains("<b>", clean);
        Assert.Contains("text-align: center", clean);
        Assert.Contains("colspan=\"2\"", clean);
        Assert.Contains("<table>", clean);
    }

    [Fact]
    public void Sanitizer_DoesNotDoubleEscapeEntitiesAcrossSaves()
    {
        // حفظٌ متكرر كان سيحوّل &amp; إلى &amp;amp; ثم &amp;amp;amp; …
        var once = DocumentHtmlSanitizer.Sanitize("<p>راتب &amp; بدل</p>");
        var twice = DocumentHtmlSanitizer.Sanitize(once);

        Assert.Equal(once, twice);
        Assert.Contains("&amp;", once);
        Assert.DoesNotContain("&amp;amp;", once);
    }

    [Fact]
    public void Sanitizer_EscapesStrayAngleBrackets()
    {
        var clean = DocumentHtmlSanitizer.Sanitize("<p>الراتب < 500 والبدل > 100</p>");

        Assert.Contains("&lt; 500", clean);
        Assert.Contains("&gt; 100", clean);
    }

    // ── الاستبدال ──────────────────────────────────────────────────────────────

    [Fact]
    public void KnownTokens_AreReplacedWithFormattedValues()
    {
        var result = DocumentTokenEngine.Render(
            "<p>{Employee-FullName} — {Employee-Code} — {Basic-Salary}</p>", Tokens());

        Assert.Contains("محمد علي", result.Html);
        Assert.Contains("E-007", result.Html);
        Assert.Contains("1,200,000.00", result.Html);
        Assert.Empty(result.UnresolvedTokens);
    }

    [Fact]
    public void UnknownToken_IsKeptVisible_AndReported()
    {
        // محوُه بصمت يُخرج وثيقةً ناقصة تبدو سليمة، والكاتب لا يكتشف خطأه أبداً.
        var result = DocumentTokenEngine.Render("<p>{Employee-Codee}</p>", Tokens());

        Assert.Contains("{Employee-Codee}", result.Html);
        Assert.Single(result.UnresolvedTokens);
        Assert.Equal("Employee-Codee", result.UnresolvedTokens[0]);
    }

    [Fact]
    public void MissingValue_RendersEmpty_NotTheWordNull()
    {
        var row = Row() with { BasicSalary = null, SalaryScale = null };
        var tokens = DocumentTokenEngine.Build(row, Extras(), Context(), AsOf);

        var result = DocumentTokenEngine.Render("<p>[{Basic-Salary}][{Salary-Scale}]</p>", tokens);

        Assert.Contains("[][]", result.Html);
        Assert.DoesNotContain("null", result.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GrossSalary_IsBasicPlusAllowances()
    {
        var result = DocumentTokenEngine.Render("{Gross-Salary}", Tokens());

        Assert.Equal("1,500,000.00", result.Html);
    }

    [Fact]
    public void CustomFields_BecomeTokensAutomatically()
    {
        var row = Row() with
        {
            CustomFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["bloodtype"] = "O+" }
        };

        var tokens = DocumentTokenEngine.Build(row, Extras(), Context(), AsOf);
        var result = DocumentTokenEngine.Render("<p>{Custom-bloodtype}</p>", tokens);

        Assert.Contains("O+", result.Html);
        Assert.Empty(result.UnresolvedTokens);
    }

    [Fact]
    public void CssBracesAreNotMistakenForTokens()
    {
        // نمط الرمز يمنع المسافات عمداً فلا يلتقط أقواس CSS ولا JSON.
        var result = DocumentTokenEngine.Render("<p>{ color: red }</p>", Tokens());

        Assert.Contains("{ color: red }", result.Html);
        Assert.Empty(result.UnresolvedTokens);
    }

    [Fact]
    public void ExtractTokens_ListsDistinctTokensOnly()
    {
        var tokens = DocumentTokenEngine.ExtractTokens("{A-1} {A-1} {B-2}");

        Assert.Equal(2, tokens.Count);
    }

    [Fact]
    public void UnknownTokens_AreDetectedAgainstCatalog()
    {
        var unknown = DocumentTokenEngine.UnknownTokens(
            "{Employee-Code} {Not-A-Token}", DocumentTokenEngine.Catalog);

        Assert.Single(unknown);
        Assert.Equal("Not-A-Token", unknown[0]);
    }

    // ── مدة الخدمة ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "أقل من شهر")]
    [InlineData(1, "شهر")]
    [InlineData(2, "شهران")]
    [InlineData(5, "5 أشهر")]
    [InlineData(12, "سنة")]
    [InlineData(24, "سنتان")]
    [InlineData(27, "سنتان و3 أشهر")]
    [InlineData(37, "3 سنوات وشهر")]
    public void ServicePeriodLabel_ReadsNaturally(int months, string expected)
    {
        Assert.Equal(expected, DocumentTokenEngine.ServicePeriodLabel(months));
    }

    [Fact]
    public void ServicePeriodLabel_ForUnknownService_IsEmpty()
    {
        Assert.Equal(string.Empty, DocumentTokenEngine.ServicePeriodLabel(null));
    }

    [Fact]
    public void EmptyTemplate_RendersEmpty_WithoutThrowing()
    {
        var result = DocumentTokenEngine.Render(null, Tokens());

        Assert.Equal(string.Empty, result.Html);
        Assert.Empty(result.UnresolvedTokens);
    }
}
