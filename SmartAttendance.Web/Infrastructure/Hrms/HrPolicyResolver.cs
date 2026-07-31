using System.Text.Json;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// طبقة «سياسة أمّ + نُسَخ مشروطة مرتّبة» — منطق نقيّ.
///
/// هذا هو النمط الذي رصدناه بكيان بأوضح صوره على شاشة فترة التجربة:
/// > «فترة التجربة على مستوى الشركة. لتغيير فترة التجربة على مستوى وحدة العمل،
/// > أنشئ نسخة.» و«اسحب السجلات لتغيير الترتيب الذي ستُنفَّذ به شروط النسخ».
///
/// ⭐ **الترتيب = الأسبقية، وأول نسخة تنطبق تفوز.** وهذا يحلّ التعارض بلا منطق
/// خفيّ: المستخدم يرى الترتيب ويغيّره بالسحب بدل أن يخمّن أيّ استثناء غلب.
///
/// **غياب هذه الطبقة هو سبب كون نظامنا أحادي السياسة**، واصطدمنا بغيابها مرّتين:
/// أوعية الاحتساب (جرّبنا «افتراضاً مخزَّناً» فتلف بصمت) وسياسة ربط الراتب بالحضور
/// (مفتاح واحد للنظام كلّه). الحلّ الصحيح أن يكون الافتراض **سياسةً أمّاً ظاهرة**
/// والاستثناءات **نُسَخاً مرئية مرتّبة** — لا حالة مخفية.
///
/// **الحمولة قاموس نصّي لا نوع مخصَّص**، ليخدم أي سياسة (تجربة · إنذار · ربط
/// الحضور · عناصر الراتب) بلا جدول جديد لكل واحدة. والنسخة تحمل **ما تغيّره فقط**
/// وتُدمج فوق الأمّ، فتغييرُ حقلٍ بالأمّ يسري على كل النُسَخ التي لم تُخصّصه.
/// </summary>
public static class HrPolicyResolver
{
    /// <summary>نسخة سياسة: شروط انطباقها + ما تغيّره + موضعها بالترتيب.</summary>
    public sealed record Override(
        int Id,
        string Name,
        int SortOrder,
        bool IsActive,
        HrConditions.ConditionSet Conditions,
        IReadOnlyDictionary<string, string> Payload);

    /// <summary>نتيجة الحسم: القيم النهائية + أيّ نسخة غلبت (أو الأمّ) + سببٌ يُعرض.</summary>
    public sealed record Resolution(
        IReadOnlyDictionary<string, string> Values,
        int? WinningOverrideId,
        string Source);

    /// <summary>
    /// يحسم السياسة لموظف واحد: أول نسخة **نشطة** تنطبق شروطها بترتيب
    /// <see cref="Override.SortOrder"/>، وإلا السياسة الأمّ.
    ///
    /// ⚠️ نسخة **بلا شروط لا تنطبق أبداً** (<c>matchWhenEmpty: false</c>) — وإلا
    /// ظلّلت الأمّ للجميع، وهو أخطر ما بهذا النمط: نسخة أُنشئت ونُسي ملء شروطها
    /// تصير هي السياسة الفعلية للشركة كلّها بلا أن يلاحظ أحد.
    /// </summary>
    public static Resolution Resolve(
        IReadOnlyDictionary<string, string> parent,
        IEnumerable<Override> overrides,
        IReadOnlyDictionary<string, HrConditions.Fact> facts)
    {
        foreach (var candidate in overrides.Where(item => item.IsActive).OrderBy(item => item.SortOrder).ThenBy(item => item.Id))
        {
            if (!HrConditions.Matches(candidate.Conditions, facts, matchWhenEmpty: false))
            {
                continue;
            }

            var merged = new Dictionary<string, string>(parent, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in candidate.Payload)
            {
                merged[key] = value;
            }

            return new Resolution(merged, candidate.Id, candidate.Name);
        }

        return new Resolution(
            new Dictionary<string, string>(parent, StringComparer.OrdinalIgnoreCase),
            null,
            "سياسة الشركة");
    }

    /// <summary>
    /// إعادة ترتيب بالسحب: المعرّفات بالترتيب الجديد ⟵ أرقام تسلسل متتالية.
    /// المعرّفات غير المذكورة تبقى بعدها بترتيبها السابق، فلا يختفي صفّ لم تُرسله
    /// الواجهة (مثلاً نسخة أُضيفت بتبويب آخر أثناء السحب).
    /// </summary>
    public static Dictionary<int, int> Reorder(IReadOnlyList<int> orderedIds, IEnumerable<Override> existing)
    {
        var result = new Dictionary<int, int>();
        var rank = 0;

        foreach (var id in orderedIds.Distinct())
        {
            result[id] = ++rank;
        }

        foreach (var item in existing.OrderBy(item => item.SortOrder).ThenBy(item => item.Id))
        {
            if (!result.ContainsKey(item.Id))
            {
                result[item.Id] = ++rank;
            }
        }

        return result;
    }

    // ── تسلسل الحمولة ──────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string SerializePayload(IReadOnlyDictionary<string, string>? payload) =>
        payload is null || payload.Count == 0
            ? string.Empty
            : JsonSerializer.Serialize(payload, JsonOptions);

    /// <summary>JSON تالف ⟹ حمولة فارغة: نسخة لا تُقرأ حمولتها تساوي الأمّ، لا تُسقط الصفحة.</summary>
    public static Dictionary<string, string> DeserializePayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            return parsed is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>الحقول التي تخالف الأمّ فعلاً — تُعرض بصفّ النسخة فيُفهم أثرها بلا فتحها.</summary>
    public static List<string> ChangedKeys(
        IReadOnlyDictionary<string, string> parent,
        IReadOnlyDictionary<string, string> payload) =>
        payload
            .Where(entry => !parent.TryGetValue(entry.Key, out var value)
                            || !string.Equals(value, entry.Value, StringComparison.Ordinal))
            .Select(entry => entry.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
}
