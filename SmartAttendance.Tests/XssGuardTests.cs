using System.IO;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حرّاس انحدار لثغرات XSS المُغلقة في Phase 11 (مسح <c>Html.Raw</c>، فرع
/// security/full-production-closure، 2026-08-11):
/// <list type="bullet">
///   <item><b>E</b> — حقن خاصّية <c>onclick</c>: كانت القيَم تُهرَّب بـ
///   <c>Replace("'", "\\'")</c> فقط داخل خاصّية بعلامات مزدوجة، فقيمة فيها
///   <c>"</c> أو <c>&lt;</c> تكسر الخاصّية. أُصلح بـ <see cref="ScriptSafe.Js"/>
///   (طبقة JS) + إسقاط <c>Html.Raw</c> (طبقة الخاصّية عبر Razor).</item>
///   <item><b>F</b> — معاينة الوثائق ومركز البطاقات كانا يعرضان ناتج محرّك
///   القوالب خاماً بلا تنقية، بعكس طريق <c>View</c> الذي يُنقّي.</item>
/// </list>
/// </summary>
public sealed class XssGuardTests
{
    // ---- E: سلوك ScriptSafe.Js (نقيّ) --------------------------------------

    [Fact]
    public void Js_EscapesBackslashAndSingleQuote()
    {
        // الخروج من سلسلة JS بعلامة مفردة يُهرَّب؛ والعكسة تُضاعَف كي لا تهرّب التالي.
        Assert.Equal("\\'); alert(1); //", ScriptSafe.Js("'); alert(1); //"));
        Assert.Equal("a\\\\b", ScriptSafe.Js("a\\b"));
    }

    [Fact]
    public void Js_EscapesLineTerminators()
    {
        Assert.Equal("a\\r\\nb", ScriptSafe.Js("a\r\nb"));
        Assert.Equal("\\u2028\\u2029", ScriptSafe.Js("\u2028\u2029"));
    }

    [Fact]
    public void Js_LeavesDoubleQuoteAndAngleForRazorLayer()
    {
        // JS لا تحتاج تهريب هذه داخل سلسلة مفردة؛ ترميز الخاصّية (Razor بلا Html.Raw)
        // يتكفّل بـ " و < و > — فالدالة تُبقيها كما هي عمداً.
        Assert.Equal("\"><img src=x onerror=alert(1)>", ScriptSafe.Js("\"><img src=x onerror=alert(1)>"));
    }

    [Fact]
    public void Js_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ScriptSafe.Js(null));
        Assert.Equal(string.Empty, ScriptSafe.Js(string.Empty));
    }

    // ---- E: حرّاس نصّية على القوالب المُصلَحة --------------------------------

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Web(params string[] segments)
    {
        var parts = new List<string> { RepoRoot(), "SmartAttendance.Web" };
        parts.AddRange(segments);
        return File.ReadAllText(Path.Combine(parts.ToArray()));
    }

    [Fact]
    public void AccessRoles_UsesScriptSafe_NotPartialQuoteEscape()
    {
        var page = Web("Pages", "AccessRoles", "Index.cshtml");
        Assert.Contains("ScriptSafe.Js(role.NameAr)", page);
        Assert.Contains("ScriptSafe.Js(role.Note)", page);
        // النمط الهشّ القديم اختفى من مقبض التعديل.
        Assert.DoesNotContain("role.NameAr.Replace(\"'\", \"\\\\'\")", page);
    }

    [Fact]
    public void EmployeeProfile_UsesScriptSafe_ForAllowanceAndContract()
    {
        var page = Web("Pages", "Employees", "Profile.cshtml");
        Assert.Contains("ScriptSafe.Js(allowance.Note)", page);
        Assert.Contains("ScriptSafe.Js(contract.ContractNo)", page);
        Assert.Contains("ScriptSafe.Js(contract.Note)", page);
        Assert.DoesNotContain(".Replace(\"'\", \"\\\\'\")", page);
    }

    // ---- F: حرّاس نصّية على تنقية ناتج محرّك القوالب --------------------------

    [Fact]
    public void DocumentPreview_SanitizesTemplateOutput()
    {
        var page = Web("Pages", "Documents", "Generate.cshtml.cs");
        Assert.Contains("DocumentHtmlSanitizer.Sanitize(html)", page);
    }

    [Fact]
    public void BadgeCenter_SanitizesTemplateOutput()
    {
        var page = Web("Pages", "BadgeCenter", "Index.cshtml.cs");
        Assert.Contains("DocumentHtmlSanitizer.Sanitize(html)", page);
    }
}
