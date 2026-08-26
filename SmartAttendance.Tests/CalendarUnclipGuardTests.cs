using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حارس نصّي على قاعدة «فكّ القصّ» بملف الكلاندر — الوحيد بالنظام الذي يفرض
/// <c>overflow: visible !important</c> على حاويات بمطابقة اسم جزئية.
///
/// <para>العطل الذي يحرسه (رُصد حيّاً 2026-08-07 بـ/AttendanceViewer): القاعدة سحقت
/// <c>overflow:auto</c> المُعلَن بالصفحة، فخرج جدول 1885px من حاوية 1003px ودفع
/// المستند لـ2198px ⟹ التمرير الأفقي يجرّ القوقعة كلها، و<c>position:sticky</c>
/// للعمود الأول يفقد مُمرِّره فيتعطّل التجميد.</para>
///
/// <para>الاختبار نصّي لا سلوكيّ عمداً: لا مُصيِّر CSS بطقم الاختبارات، والقيمة هنا
/// أن يفشل البناء إن أُعيدت الكاسحة بلا مخرج طوارئ — لا أن يُحاكى المتصفّح.</para>
/// </summary>
public class CalendarUnclipGuardTests
{
    private const string OptOut = "[data-zy-scroll]";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>
    /// نصّ ملف الكلاندر **بلا تعليقات**.
    ///
    /// <para>الحذف مقصود: الحارس يفحص القواعد لا التوثيق. وثّقنا القاعدة الكاسحة
    /// المحذوفة باقتباسها حرفياً داخل تعليق (<c>.nxcal-ready [class*="card"] { … }</c>)
    /// فصار التعليق نفسه يطابق نمط «قاعدة فكّ قصّ بلا مخرج طوارئ» ويُسقط الاختبارين.
    /// أي حارسٍ يقرأ البرمجة والنثر معاً يعاقب على شرح الخطأ كما يعاقب على ارتكابه.</para>
    /// </summary>
    private static string CalendarCss()
    {
        var css = File.ReadAllText(Path.Combine(
            RepoRoot(), "SmartAttendance.Web", "wwwroot", "css", "nexora-calendar-v18.css"));

        return Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
    }

    /// <summary>كل مُحدِّد يفرض overflow:visible!important يجب أن يحمل مخرج الطوارئ.</summary>
    [Fact]
    public void EveryUnclipSelectorCarriesTheOptOut()
    {
        var css = CalendarCss();

        // كتل القواعد التي تفرض overflow: visible !important
        var blocks = Regex.Matches(css, @"([^{}]+)\{[^}]*overflow\s*:\s*visible\s*!important[^}]*\}")
            .Select(m => m.Groups[1].Value);

        foreach (var selectorList in blocks)
        {
            var selectors = selectorList
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0 && s.Contains("nxcal-ready"));

            foreach (var selector in selectors)
            {
                Assert.True(
                    selector.Contains(OptOut),
                    $"المُحدِّد «{selector}» يفرض overflow:visible!important بلا مخرج {OptOut} — "
                    + "سيسحق تمرير أي حاوية اسمها يحوي card/panel/form/field/section.");
            }
        }
    }

    /// <summary>الحاوية التي رُصد عطلها فعلاً يجب أن تبقى موسومة.</summary>
    [Fact]
    public void AttendanceViewerMatrixKeepsItsScrollOwnership()
    {
        var page = RazorPageAssetReader.ReadWithLinkedPageCss("AttendanceViewer", "Index.cshtml");

        Assert.Matches(@"class=""avw-matrix-card""\s+data-zy-scroll", page);

        // احتواء أفقيّ فقط — ولا `max-height` عليها: المستند هو المُمرِّر رأسياً،
        // وتقييد ارتفاعها كان يقصّ صفحة الـ20 موظفاً إلى ستّة صفوف مرئية.
        Assert.Matches(@"\.avw-matrix-card\s*\{[^}]*overflow-x:\s*auto", page);
        Assert.DoesNotMatch(@"\.avw-matrix-card\s*\{[^}]*max-height", page);
    }

    /// <summary>
    /// فكّ القصّ يخصّ **أسلاف** المنسدلة وحدهم. مطابقة أسماء السلائل
    /// (<c>[class*="card"]</c> وأخواتها) كانت تصيب إخوةً وأعماماً لا علاقة لهم
    /// بالمنسدلة — 58 صنف حاوية تعلن overflow خاصاً بها بالقياس (2026-08-07).
    /// </summary>
    [Fact]
    public void UnclipTargetsAncestorsOnly_NotDescendantsByNameMatch()
    {
        var css = CalendarCss();

        var selectors = Regex
            .Matches(css, @"([^{}]+)\{[^}]*overflow\s*:\s*visible\s*!important[^}]*\}")
            .SelectMany(m => m.Groups[1].Value.Split(','))
            .Select(s => s.Trim())
            .Where(s => s.Contains("nxcal-ready"));

        foreach (var selector in selectors)
        {
            Assert.False(
                Regex.IsMatch(selector, @"nxcal-ready\s+\S"),
                $"المُحدِّد «{selector}» يفكّ القصّ عن **سليل** لـ.nxcal-ready. "
                + "القاصّ هو السلف لا السليل، و`markParents` يعلّم السلسلة الأبوية "
                + "بدقّة — فمطابقة أسماء السلائل تسلب حاوياتٍ بريئةً تمريرها.");
        }
    }

    /// <summary>
    /// فكّ القصّ **لحظيّ**: مشروط بـ<c>html.nxcal-open</c> التي يرفعها السكربت عند
    /// الفتح ويُنزلها عند الإغلاق. بلا الشرط تفقد الحاوية تمريرها طوال عمر الصفحة
    /// من أجل منسدلةٍ تظهر لحظات.
    /// </summary>
    [Fact]
    public void UnclipIsGatedOnTheOpenState()
    {
        Assert.Matches(@"html\.nxcal-open\s+\.nxcal-ready", CalendarCss());

        // والراية تُرفع وتُنزل بكلا المنتقيين — منتقي التاريخ ومنتقي الشهر يتشاركان
        // الصنف `.nxcal`، فلو رفعها أحدهما ولم يُنزلها الآخر بقي القصّ مفكوكاً.
        foreach (var script in new[] { "nexora-calendar-v18.js", "nexora-month-picker-v1.js" })
        {
            var source = File.ReadAllText(Path.Combine(
                RepoRoot(), "SmartAttendance.Web", "wwwroot", "js", script));

            Assert.Contains("nxcal-open", source);
            Assert.Contains("syncOpenState", source);
        }
    }
}
