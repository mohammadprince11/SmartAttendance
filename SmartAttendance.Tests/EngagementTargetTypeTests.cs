using System.Text.RegularExpressions;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حارس عقد «نوع الاستهداف» بشاشة تفاعل الموظف.
///
/// **العطل الذي وُلد منه (2026-08-02)**: الواجهة الوحيدة الحيّة
/// (<c>Pages/Engagement/Index.cshtml</c>) كانت ترسل <c>TargetType="Employees"</c>
/// بالجمع، بينما الخادم كلّه يقارن بـ<c>"Employee"</c> بالمفرد في أربعة مواضع:
/// <c>ValidateTarget</c> · <c>BuildTargetValue</c> · بناء طلب الإنشاء ·
/// تسمية الاستهداف بالقائمة.
///
/// والنتيجة أسوأ من رفضٍ صريح: <c>ValidateTarget</c> لا يطابق أيّ فرع فيرجع
/// «صالح»، فحارس «اختر موظفاً واحداً على الأقل» **لا يعمل إطلاقاً**، وقائمة
/// المستلمين تصير فارغة، ويُعلَن النجاح. إعلانٌ إلى لا أحد.
///
/// ولأن الطرفين يُصرَّفان بلا خطأ بناء — نصٌّ بالواجهة ونصٌّ بالكود — لا يكشفه
/// إلا حارسٌ يقارنهما. لذلك هذا الاختبار يقرأ القيم الفعلية من الملفّين.
/// </summary>
public class EngagementTargetTypeTests
{
    /// <summary>القيم التي يعرف الخادم كيف يتصرّف حيالها.</summary>
    private static readonly string[] Known = { "All", "Employee", "Department", "Branch" };

    [Fact]
    public void TargetTypeOptions_InTheLiveUi_AreAllUnderstoodByTheServer()
    {
        var markup = File.ReadAllText(
            Path.Combine(WebRoot(), "Pages", "Engagement", "Index.cshtml"));

        var emitted = TargetTypeOptionValues(markup);

        Assert.NotEmpty(emitted);

        var unknown = emitted.Where(v => !Known.Contains(v, StringComparer.Ordinal)).ToArray();

        Assert.True(
            unknown.Length == 0,
            "قيمُ استهدافٍ لا يفهمها الخادم فتُسقِط المستلمين بصمت: " +
            string.Join(", ", unknown));
    }

    [Fact]
    public void EveryKnownTargetType_IsOfferedByTheUi()
    {
        var markup = File.ReadAllText(
            Path.Combine(WebRoot(), "Pages", "Engagement", "Index.cshtml"));

        var emitted = TargetTypeOptionValues(markup);

        foreach (var known in Known)
        {
            Assert.Contains(known, emitted);
        }
    }

    [Fact]
    public void ServerComparisons_StillUseTheSameVocabulary()
    {
        // لو غيّر أحدٌ الخادم بدل الواجهة، يجب أن يسقط الاختبار كذلك.
        var shared = File.ReadAllText(
            Path.Combine(WebRoot(), "Pages", "Engagement", "EngagementPageModel.cs"));
        var announcements = File.ReadAllText(
            Path.Combine(WebRoot(), "Pages", "Engagement", "Announcements.cshtml.cs"));

        Assert.Contains("\"Employee\"", shared);
        Assert.Contains("\"Employee\"", announcements);
    }

    /// <summary>يستخرج قيم <c>&lt;option value="…"&gt;</c> داخل منسدلات TargetType.</summary>
    private static string[] TargetTypeOptionValues(string markup)
    {
        var values = new List<string>();

        foreach (Match select in Regex.Matches(
                     markup,
                     "<select[^>]*name=\"[^\"]*TargetType\"[^>]*>(.*?)</select>",
                     RegexOptions.Singleline))
        {
            foreach (Match option in Regex.Matches(
                         select.Groups[1].Value,
                         "<option[^>]*value=\"([^\"]*)\""))
            {
                values.Add(option.Groups[1].Value);
            }
        }

        return values.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string WebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "SmartAttendance.Web");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "تعذّر إيجاد مجلّد SmartAttendance.Web من " + AppContext.BaseDirectory);
    }
}
