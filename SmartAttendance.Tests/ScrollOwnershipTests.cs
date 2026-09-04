using System.Text.RegularExpressions;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حارس مِلكية التمرير: المستند وحده يمرّر، بكل المقاسات.
///
/// **العطل الذي وُلد منه**: القوقعة كانت تقفل <c>html</c>/<c>body</c> بارتفاع
/// الشاشة و<c>overflow: hidden</c>، وتضع الصفحة داخل مُمرِّرٍ متداخل هو
/// <c>&lt;main class="zynora-content"&gt;</c>. فصار للصفحة الواحدة سلوكان: مُمرِّر
/// داخليّ بسطح المكتب، وتمرير مستندٍ عاديّ تحت 980px (لأن استعلام الوسائط كان
/// يفكّ القفل). ومعه: التصاقات الصفحات تموت داخل المُمرِّر، والتمرير الأصلي
/// للمتصفح (لوحة المفاتيح · استعادة الموضع · <c>#anchor</c>) يخاطب حاويةً غير
/// التي يراها المستخدم، وكودُ الجافاسكربت اضطُرّ لفرعين لكل قفزة.
///
/// العطل صامت: لا خطأ بناء ولا استثناء — فقط تعريفُ CSS واحد يعيد المُمرِّر
/// المتداخل. فالحارس هنا يقرأ التعريفات نفسها.
/// </summary>
public class ScrollOwnershipTests
{
    /// <summary>الأصناف التي تُشكّل عمود القوقعة بين الجذر والصفحة.</summary>
    private static readonly string[] ShellClasses =
    [
        ".zynora-shell",
        ".zynora-main",
        ".zynora-content"
    ];

    /// <summary>
    /// لا شيء بعمود القوقعة يصنع حاوية تمرير.
    ///
    /// أي <c>overflow</c> غير <c>visible</c> — حتى <c>clip</c> على محورٍ واحد —
    /// يحوّل العنصر لحاوية تمرير ويقتل <c>position: sticky</c> لكل ما بداخله،
    /// لذلك يُرفَض المحور الأفقيّ أيضاً لا الرأسيّ وحده. الحماية الأفقية مركزها
    /// <c>&lt;html&gt;</c> وحده.
    /// </summary>
    [Fact]
    public void ShellChain_NeverBecomesAScrollContainer()
    {
        var offenders = new List<string>();

        foreach (var (file, selector, body) in ReadRules())
        {
            if (!ShellClasses.Any(cls => SelectorTargets(selector, cls)))
            {
                continue;
            }

            foreach (var declaration in Declarations(body))
            {
                var (property, value) = declaration;

                if (property is "overflow" or "overflow-x" or "overflow-y"
                    && value is not "visible")
                {
                    offenders.Add($"{file}: {selector} ⟵ {property}: {value}");
                }

                // ارتفاعٌ مقفول بالشاشة = المحتوى يفيض داخل العنصر بدل أن يُطيل
                // المستند، وهو النصف الآخر من المُمرِّر المتداخل.
                if (property is "height" or "max-height"
                    && (value.Contains("100vh", StringComparison.Ordinal)
                        || value.Contains("100dvh", StringComparison.Ordinal)))
                {
                    offenders.Add($"{file}: {selector} ⟵ {property}: {value}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "عمود القوقعة يجب أن يبقى بلا تمرير ولا ارتفاعٍ مقفول (استعمل min-height):\n"
                + string.Join('\n', offenders));
    }

    /// <summary>
    /// جذر المستند يبقى قابلاً للتمرير.
    ///
    /// نقبل قفل التمرير عند فتح النوافذ لأنه مشروطٌ بصنف (<c>html.…-modal-open</c>
    /// و<c>html:has(&gt; body[class*="modal-open"])</c>)، ونرفض القفل غير المشروط.
    /// </summary>
    [Fact]
    public void DocumentRoot_IsNeverLockedUnconditionally()
    {
        var offenders = new List<string>();

        foreach (var (file, selector, body) in ReadRules())
        {
            if (!IsBareDocumentRoot(selector))
            {
                continue;
            }

            foreach (var (property, value) in Declarations(body))
            {
                if (property is "overflow" or "overflow-y"
                    && value is "hidden" or "clip")
                {
                    offenders.Add($"{file}: {selector} ⟵ {property}: {value}");
                }

                if (property is "height" or "max-height"
                    && value is not "auto" and not "none")
                {
                    offenders.Add($"{file}: {selector} ⟵ {property}: {value}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "قفل تمرير المستند يجب أن يكون مشروطاً بصنف نافذة فقط:\n"
                + string.Join('\n', offenders));
    }

    /// <summary>
    /// المُحدِّد يستهدف <c>html</c>/<c>body</c> عارياً — بلا صنف ولا شرط.
    /// وجود أي <c>.</c> أو <c>:</c> أو <c>[</c> يعني قاعدةً مشروطة (قفل نافذة).
    /// </summary>
    private static bool IsBareDocumentRoot(string selector) =>
        selector
            .Split(',')
            .Select(part => part.Trim())
            .Any(part => part is "html" or "body");

    /// <summary>
    /// المُحدِّد يضبط الصنف نفسه لا وريثاً له: <c>.zynora-content</c> نعم،
    /// و<c>.zynora-content .zy-card</c> لا (البطاقة قد تمرّر بداخلها بحرّية).
    /// </summary>
    private static bool SelectorTargets(string selector, string cls) =>
        selector
            .Split(',')
            .Select(part => part.Trim())
            .Any(part => part.EndsWith(cls, StringComparison.Ordinal)
                      || part.EndsWith(cls + " > *", StringComparison.Ordinal));

    private static IEnumerable<(string File, string Selector, string Body)> ReadRules()
    {
        foreach (var path in Directory.EnumerateFiles(CssRoot(), "*.css"))
        {
            var css = StripComments(File.ReadAllText(path));
            var name = Path.GetFileName(path);

            foreach (Match match in Regex.Matches(css, @"([^{}]+)\{([^{}]*)\}"))
            {
                var selector = match.Groups[1].Value;

                // بكتل @media يلتقط التعبير أعلاه مقدّمة الاستعلام مع أول محدِّد
                // بداخلها؛ نقتطعها فيبقى المحدِّد وحده.
                var lastBrace = selector.LastIndexOf('{');
                if (lastBrace >= 0)
                {
                    selector = selector[(lastBrace + 1)..];
                }

                yield return (name, Normalize(selector), match.Groups[2].Value);
            }
        }
    }

    private static IEnumerable<(string Property, string Value)> Declarations(string body)
    {
        foreach (var raw in body.Split(';'))
        {
            var separator = raw.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            var property = raw[..separator].Trim().ToLowerInvariant();
            var value = raw[(separator + 1)..]
                .Replace("!important", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim()
                .ToLowerInvariant();

            yield return (property, value);
        }
    }

    private static string StripComments(string css) =>
        Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    private static string Normalize(string selector) =>
        Regex.Replace(selector, @"\s+", " ").Trim();

    private static string CssRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "SmartAttendance.Web", "wwwroot", "css");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "تعذّر إيجاد wwwroot/css من " + AppContext.BaseDirectory);
    }
}
