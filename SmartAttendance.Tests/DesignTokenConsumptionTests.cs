using System.Text.RegularExpressions;
using SmartAttendance.Web.Infrastructure.Theming;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حارس «المِزلاج الصمّاء» بمختبر المقاسات.
///
/// **العطل الذي وُلد منه**: طبقة الاستهلاك كانت تضبط المنسدلة بالمحدِّد
/// <c>.zy-scope .nxcs</c>، و<c>nxcs</c> صنفٌ **لا وجود له بالمستودع** — الغلاف
/// الحقيقي <c>nxcs-select</c>. فكانت مجموعة «المنسدلة» بالمختبر تُخزَّن بالقاعدة
/// وتظهر بكتلة <c>:root</c> ولا يقرؤها أحد: قيس حيّاً <c>--zy-dropdown-h:30px</c>
/// بينما المنسدلة على الشاشة 48px. ثلاثة مزاليج تتحرّك ولا تحرّك شيئاً — وهو
/// بالضبط ما ينتقده <see cref="DesignTokenCatalog"/> على غيره.
///
/// ولأن العطل صامت (لا خطأ بناء ولا استثناء وقت التشغيل، فقط مقياسٌ لا يفعل
/// شيئاً) فلا يكشفه إلا حارسٌ يقارن الطرفين. هذان اختباران:
/// كل توكن له مستهلك · وكل صنفٍ يستهلكه موجود فعلاً بالواجهة.
/// </summary>
public class DesignTokenConsumptionTests
{
    private static readonly string TokensCss = ReadTokensCss();
    private static readonly Lazy<string> UiSources = new(ReadUiSources);

    /// <summary>
    /// كل توكن بالكتالوج يجب أن يُقرأ بـ<c>var(--zy-…)</c> مرّةً على الأقل.
    /// توكنٌ بلا قارئ = مِزلاجٌ بالمختبر بلا أثر.
    /// </summary>
    [Fact]
    public void EveryCatalogToken_IsReadBySomeRule()
    {
        var orphans = DesignTokenCatalog.All
            .Where(token => !TokensCss.Contains($"var(--zy-{token.Key}", StringComparison.Ordinal))
            .Select(token => token.Key)
            .ToArray();

        Assert.True(
            orphans.Length == 0,
            "توكنات بلا مستهلك بـzynora-tokens.css (مزاليج صمّاء بالمختبر): "
                + string.Join(", ", orphans));
    }

    /// <summary>
    /// كل صنف CSS تستهدفه طبقة الاستهلاك يجب أن يكون مكتوباً فعلاً بواجهةٍ ما.
    /// صنفٌ لا يُنتجه أحد = قاعدةٌ لا تطابق شيئاً، فالتوكن الذي تقرؤه صامّ.
    ///
    /// نبحث بالـ<c>.cshtml</c> و<c>.cs</c> و<c>.js</c> لأن أغلفة الودجتات عندنا
    /// تُبنى بالجافاسكربت (<c>wrapper.className = "nxcs-select"</c>) لا بالرِّزم.
    /// </summary>
    [Fact]
    public void EveryTargetedClass_ExistsInTheUi()
    {
        // أصناف بنيوية يعرّفها ملف التوكنات نفسه أو التخطيط، لا الودجتات.
        //
        // **القائمة بالاسم عمداً وتبقى قصيرة**: قاعدةٌ عامّة من نوع «كل ما يعرّفه
        // نظام التصميم مقبول» كانت ستُعيد تمرير العطل الأصليّ، إذ يظهر `nxcs`
        // بذلك الملف عشر مرّات. كل إضافة هنا تستوجب قراراً بشرياً — وهذه وظيفة
        // الحارس لا عائقُه.
        var infrastructure = new HashSet<string>(StringComparer.Ordinal)
        {
            "zy-scope", "zy-scrolling",
        };

        // التعليقات تقتبس محدِّدات ملفاتٍ أخرى للشرح (مثل `:not(.no-zy-check)`)،
        // وهي ليست قواعد فلا تُحاسَب. بلا هذا يفشل الحارس على توثيقٍ صحيح.
        var rulesOnly = Regex.Replace(TokensCss, @"/\*.*?\*/", " ", RegexOptions.Singleline);

        var missing = Regex.Matches(rulesOnly, @"\.(?<cls>[a-zA-Z][\w-]*)")
            .Select(match => match.Groups["cls"].Value)
            .Where(cls => !infrastructure.Contains(cls))
            .Distinct(StringComparer.Ordinal)
            .Where(cls => !IsProducedByUi(cls))
            .OrderBy(cls => cls, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "أصناف تستهدفها zynora-tokens.css ولا تُنتَج بأي واجهة (قواعد ميتة): "
                + string.Join(", ", missing));
    }

    /// <summary>
    /// المطابقة بحدود الكلمة لا بالاحتواء. لولا ذلك لمرّ العطلُ الأصليّ نفسه:
    /// <c>nxcs</c> جزءٌ نصّيّ من <c>nxcs-select</c> الموجود بالجافاسكربت، فيبدو
    /// الصنفُ الميت «موجوداً» ويصمت الحارس عن العطل الذي وُجد لأجله.
    /// </summary>
    private static bool IsProducedByUi(string cls) =>
        Regex.IsMatch(UiSources.Value, $@"(?<![\w-]){Regex.Escape(cls)}(?![\w-])");

    private static string ReadTokensCss() =>
        File.ReadAllText(Path.Combine(WebRoot(), "wwwroot", "css", "zynora-tokens.css"));

    /// <summary>
    /// كل ما يمكن أن يُنتج صنفاً بالواجهة، مقروءاً مرّةً واحدة ومخزَّناً.
    /// نستثني <c>wwwroot/css</c> عمداً: وجود الصنف بملف CSS آخر لا يعني أن أحداً
    /// يُصدره بالـHTML — وهذا بالضبط ما جعل <c>.nxcs</c> يمرّ سنةً بلا كشف.
    /// </summary>
    private static string ReadUiSources()
    {
        var root = WebRoot();
        var files = new[] { "*.cshtml", "*.cs", "*.js" }
            .SelectMany(pattern => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        return string.Join('\n', files.Select(File.ReadAllText));
    }

    private static string WebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "SmartAttendance.Web");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("تعذّر إيجاد مجلّد SmartAttendance.Web من " + AppContext.BaseDirectory);
    }
}
