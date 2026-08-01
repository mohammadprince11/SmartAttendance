using System.Text;
using System.Text.RegularExpressions;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// منقّح HTML بقائمة بيضاء لنصّ قوالب الوثائق — منطق نقيّ.
///
/// <b>لماذا لا غنى عنه:</b> نصّ القالب HTML يكتبه مستخدم (HR) ويُعرض لاحقاً
/// **للموظفين** بصفحة الوثيقة. تخزينُ HTML وعرضُه خاماً = **ثغرة XSS مخزَّنة**:
/// وسم <c>&lt;script&gt;</c> أو <c>onerror=</c> بقالبٍ واحد يسرق جلسة كل من يفتح
/// وثيقةً مولَّدة منه. وهذا بالضبط سبب إبقائي نصّ الإقرارات نصّاً صِرفاً سابقاً —
/// وهنا لا مفرّ من HTML، فيلزم التنقية.
///
/// <b>النهج: قائمة بيضاء لا سوداء.</b> القائمة السوداء تُهزَم دائماً بوسمٍ لم
/// يخطر بالبال؛ والبيضاء تسمح بالمعروف فقط وتُسقط ما عداه. النتيجة قد تكون أفقر
/// تنسيقاً، لكنها لا تكون خطرة.
///
/// ⚠️ هذا منقّح **متحفّظ ومحدود**، لا بديل عن مكتبة ناضجة (HtmlSanitizer/AngleSharp)
/// إن تُبنّيت لاحقاً. وهو يعمل على شجرة نصّية بالتعابير النمطية، فيُبقي فقط ما
/// يفهمه ويحذف كل ما سواه — والحذف هو السلوك الافتراضي عند أي شكّ.
/// </summary>
public static class DocumentHtmlSanitizer
{
    /// <summary>الوسوم المسموحة — تنسيق مستند لا تطبيق ويب.</summary>
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "hr", "div", "span",
        "b", "strong", "i", "em", "u", "s", "strike", "sub", "sup",
        "h1", "h2", "h3", "h4", "h5", "h6",
        "ul", "ol", "li",
        "table", "thead", "tbody", "tfoot", "tr", "th", "td",
        "blockquote", "pre", "code", "small"
    };

    /// <summary>
    /// الخصائص المسموحة. <b>لا <c>href</c> ولا <c>src</c> ولا <c>id</c>:</b>
    /// الروابط تفتح <c>javascript:</c> والصور تفتح <c>onerror</c>، وكلاهما لا يلزم
    /// لوثيقة مطبوعة. الصور تُدرَج لاحقاً بمسار محكوم من النظام لا بـHTML حرّ.
    /// </summary>
    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "style", "class", "colspan", "rowspan", "align", "dir"
    };

    /// <summary>خصائص style المسموحة — تنسيق بصري بحت بلا سلوك.</summary>
    private static readonly HashSet<string> AllowedStyleProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "color", "background-color", "font-size", "font-weight", "font-style", "font-family",
        "text-align", "text-decoration", "line-height", "letter-spacing",
        "margin", "margin-top", "margin-bottom", "margin-left", "margin-right",
        "padding", "padding-top", "padding-bottom", "padding-left", "padding-right",
        "border", "border-collapse", "width", "direction", "list-style-type"
    };

    private static readonly Regex TagPattern = new(@"<\s*(/?)\s*([A-Za-z][A-Za-z0-9]*)((?:[^>""']|""[^""]*""|'[^']*')*)>", RegexOptions.Compiled);
    private static readonly Regex AttributePattern = new(@"([A-Za-z\-:]+)\s*=\s*(""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.Compiled);

    /// <summary>
    /// عناصر تُحذف **بمحتواها**: إبقاء نصّ سكربتٍ بعد إسقاط وسمه يترك كوداً معروضاً،
    /// وإبقاء محتوى <c>&lt;style&gt;</c> يسرّب CSS للصفحة المضيفة.
    /// </summary>
    private static readonly Regex DangerousBlocks =
        new(@"<\s*(script|style|iframe|object|embed|form|svg|math|link|meta|base|template)\b[\s\S]*?(<\s*/\s*\1\s*>|$)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HtmlComments = new(@"<!--[\s\S]*?-->", RegexOptions.Compiled);

    public static string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var working = DangerousBlocks.Replace(html, string.Empty);
        working = HtmlComments.Replace(working, string.Empty);

        // كل وسم لا يطابق نمط الوسم السليم يُهرَّب لا يُترك: `<` شاردة يجب أن تُعرض
        // كنصّ لا أن تفتح وسماً نصفَ مكتوب يبتلع بقية الوثيقة.
        var builder = new StringBuilder(working.Length);
        var index = 0;

        foreach (Match match in TagPattern.Matches(working))
        {
            builder.Append(EscapeStrayAngles(working[index..match.Index]));
            index = match.Index + match.Length;

            var isClosing = match.Groups[1].Value == "/";
            var tag = match.Groups[2].Value.ToLowerInvariant();

            if (!AllowedTags.Contains(tag))
            {
                continue; // الوسم يُسقَط ومحتواه النصّي يبقى
            }

            if (isClosing)
            {
                builder.Append("</").Append(tag).Append('>');
                continue;
            }

            builder.Append('<').Append(tag);
            builder.Append(SanitizeAttributes(match.Groups[3].Value));
            builder.Append('>');
        }

        builder.Append(EscapeStrayAngles(working[index..]));
        return builder.ToString();
    }

    private static string SanitizeAttributes(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (Match match in AttributePattern.Matches(raw))
        {
            var name = match.Groups[1].Value;

            // كل on* يُسقَط قبل أي فحص آخر — onclick/onerror/onload هي ناقل XSS الأول.
            if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || !AllowedAttributes.Contains(name))
            {
                continue;
            }

            var value = match.Groups[3].Success ? match.Groups[3].Value
                      : match.Groups[4].Success ? match.Groups[4].Value
                      : match.Groups[5].Value;

            if (string.Equals(name, "style", StringComparison.OrdinalIgnoreCase))
            {
                value = SanitizeStyle(value);
                if (value.Length == 0) continue;
            }
            else if (ContainsScriptScheme(value))
            {
                continue;
            }

            builder.Append(' ').Append(name.ToLowerInvariant())
                   .Append("=\"").Append(DocumentTokenEngine.HtmlEscape(value)).Append('"');
        }

        return builder.ToString();
    }

    private static string SanitizeStyle(string style)
    {
        var kept = new List<string>();

        foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = declaration.IndexOf(':');
            if (separator <= 0) continue;

            var property = declaration[..separator].Trim();
            var value = declaration[(separator + 1)..].Trim();

            if (!AllowedStyleProperties.Contains(property)) continue;

            // url() و expression() و javascript: كلها نواقل تنفيذ داخل CSS.
            if (value.Contains("url(", StringComparison.OrdinalIgnoreCase)
                || value.Contains("expression", StringComparison.OrdinalIgnoreCase)
                || ContainsScriptScheme(value))
            {
                continue;
            }

            kept.Add($"{property.ToLowerInvariant()}: {value}");
        }

        return string.Join("; ", kept);
    }

    private static bool ContainsScriptScheme(string value)
    {
        var compact = new string(value.Where(c => !char.IsWhiteSpace(c) && c != '\0').ToArray());
        return compact.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("vbscript:", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("data:text/html", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>كيان HTML سليم — يُترك كما هو فلا يصير <c>&amp;amp;</c> عند كل حفظ.</summary>
    private static readonly Regex ValidEntity = new(@"&(#[0-9]{1,7}|#[xX][0-9a-fA-F]{1,6}|[A-Za-z][A-Za-z0-9]{1,31});", RegexOptions.Compiled);

    /// <summary>
    /// يهرّب النصّ الواقع بين الوسوم. <c>&amp;</c> تُهرَّب **إلا** إن كانت بداية كيان
    /// سليم — وإلا تضاعف التهريب مع كل حفظ حتى يمتلئ النصّ بـ<c>&amp;amp;amp;</c>.
    /// </summary>
    private static string EscapeStrayAngles(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var index = 0;

        foreach (Match entity in ValidEntity.Matches(text))
        {
            builder.Append(EscapeAll(text[index..entity.Index]));
            builder.Append(entity.Value);
            index = entity.Index + entity.Length;
        }

        builder.Append(EscapeAll(text[index..]));
        return builder.ToString();
    }

    private static string EscapeAll(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
