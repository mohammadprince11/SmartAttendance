using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// وعاء احتساب الخصم التأديبي — **يُركَّب لكل جزاءٍ على حدة**.
///
/// كان الخصم يُحتسب على الراتب الأساسي وحده و«اليومي» يعني <c>Basic ÷ 30</c>
/// مثبّتاً بالكود. وكيان تُظهر العكس: لكل جزاء قائمة بعناصر الراتب مع **نسبة
/// لكلٍّ** («50% من الأساسي + 100% من بدل السكن»). وهذا ليس ترفاً — الخصم على
/// الأساسي وحده أو على الإجمالي كلّه فرقٌ يظهر بكل قسيمة، والقرار قرار الشركة
/// لا قرار الكود.
///
/// ⚠️ **الفراغ يعني الأساسي 100%** لا صفراً: صفوف الجزاءات القديمة بلا وعاء
/// محفوظ يجب أن تُنتج **نفس رقم اليوم بالضبط**، وإلا انحرفت كل قسيمة صامتةً.
/// </summary>
public static class PenaltyBasePool
{
    /// <summary>عنصرٌ بالوعاء: مفتاح مكوّن من <see cref="SalaryBaseComposer"/> ونسبته.</summary>
    public sealed class Member
    {
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("percent")] public decimal Percent { get; set; }
    }

    /// <summary>سلوك ما قبل هذه الميزة: الأساسي كاملاً ولا شيء غيره.</summary>
    public static readonly IReadOnlyList<Member> Default = new[]
    {
        new Member { Key = "Basic", Percent = 100m }
    };

    public static IReadOnlyList<Member> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Default;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<Member>>(json);

            // قائمة محفوظة لكنها فارغة تعني «لم يُختَر شيء» — والوعاء الفارغ يعطي
            // خصماً صفرياً بصمت. الأسلم أن يعود للافتراضي المعلن.
            if (parsed is null || parsed.Count == 0)
            {
                return Default;
            }

            return parsed
                .Where(m => !string.IsNullOrWhiteSpace(m.Key))
                .Select(m => new Member { Key = m.Key.Trim(), Percent = m.Percent })
                .ToList();
        }
        catch (JsonException)
        {
            // JSON تالف ⟹ الافتراضي لا استثناء: صفٌّ لا يُقرأ يجب ألا يُسقط المسير.
            return Default;
        }
    }

    public static string Serialize(IEnumerable<Member>? members)
    {
        var list = (members ?? Array.Empty<Member>())
            .Where(m => !string.IsNullOrWhiteSpace(m.Key) && m.Percent != 0)
            .ToList();

        return list.Count == 0 ? string.Empty : JsonSerializer.Serialize(list);
    }

    /// <summary>
    /// مبلغ الوعاء = Σ (مبلغ المكوّن × نسبته ÷ 100).
    ///
    /// النِّسب **لا تُقيَّد بمئة بالمجموع**: «100% من الأساسي + 100% من بدل السكن»
    /// وعاءٌ مشروع، و«200% من الأساسي» كذلك إن أرادته الشركة. التقييد هنا كان
    /// سيمنع حالاتٍ حقيقية بلا أن يمنع خطأً واحداً.
    /// </summary>
    public static decimal Amount(SalaryBaseComposer.Amounts? amounts, IEnumerable<Member>? members)
    {
        if (amounts is null) return 0m;

        decimal total = 0m;
        foreach (var member in members ?? Default)
        {
            if (string.IsNullOrWhiteSpace(member.Key)) continue;
            total += amounts.Of(member.Key.Trim()) * (member.Percent / 100m);
        }

        return decimal.Round(total, 2);
    }

    /// <summary>وصف عربي مقروء للوعاء — يُعرض بالشاشة وبالكتاب الرسمي.</summary>
    public static string Describe(IEnumerable<Member>? members)
    {
        var list = (members ?? Default).Where(m => !string.IsNullOrWhiteSpace(m.Key)).ToList();
        if (list.Count == 0) return "الراتب الأساسي 100%";

        return string.Join(" + ", list.Select(m =>
        {
            var label = SalaryBaseComposer.Components
                .FirstOrDefault(c => string.Equals(c.Key, m.Key, StringComparison.OrdinalIgnoreCase)).Label ?? m.Key;
            return $"{label} {m.Percent:0.##}%";
        }));
    }
}
