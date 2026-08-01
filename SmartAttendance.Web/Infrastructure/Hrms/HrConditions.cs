using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// محرّك الشروط العام (نظير <c>lstGroupConditions</c> بكيان) — منطق نقيّ بلا قاعدة
/// بيانات: مجموعاتُ قواعدَ تُقيَّم على «حقائق» موظف فتعطي نعم/لا.
///
/// **لماذا عامّ؟** جرد كيان أظهر أن نفس البنية تخدم عندهم: أهلية الطلبات المخصصة ·
/// نُسَخ فترة التجربة والإنذار · شروط عناصر الراتب · قواعد المخالفات · أسباب
/// الإيقاف. وعندنا اليوم **كل ميزة تعيد اختراع شرطها** (<see cref="RequestTypeStore"/>
/// يعرف الجنس ومدة الخدمة فقط، و<see cref="ShiftRuleStore"/> له قواعده الخاصة)،
/// فأي معيار جديد يعني تعديل كل ميزة على حدة. هذا الملف يكسر ذلك التكرار.
///
/// **المنطق:** AND داخل المجموعة الواحدة، OR بين المجموعات — النمط القياسي لبواني
/// القواعد، وهو ما رصدناه حرفياً بكيان («انقر مجموعة قواعد جديدة… في حال توافقها»).
///
/// ⚠️ **قرار محوري: الحقيقة المفقودة لا تُطابِق أبداً — بأي عامل، حتى «لا يساوي».**
/// موظفٌ بلا راتب أساسي مسجَّل ليس «راتبه ≠ 500»، بل **مجهول**. ولو طابقت المفقودةُ
/// النفيَ لدخل 1355 موظفاً بلا راتب في كل استثناء منفيّ بصمت. الأثر العملي أن نسخة
/// السياسة **لا تنطبق** فيُرجَع للسياسة الأمّ — وهو الفشل الآمن الصحيح.
/// </summary>
public static class HrConditions
{
    // ── نموذج القيمة ───────────────────────────────────────────────────────────

    /// <summary>نوع قيمة المعيار — يقرّر كيف تُقارَن ولأي عوامل يصلح.</summary>
    public enum ValueKind
    {
        Number,
        Text,
        Date,
        Bool,
        /// <summary>نصّ مصدره قائمة تعريفات (يُقارَن كنصّ، ويُعرَض كمنسدلة).</summary>
        Lookup,
        /// <summary>معرّف رقمي مصدره جدول (فرع/قسم/منصب) — يُقارَن كرقم ويُعرَض كمنسدلة.</summary>
        Reference
    }

    public enum Operator
    {
        Equal,
        NotEqual,
        LessThan,
        GreaterThan,
        LessOrEqual,
        GreaterOrEqual,
        /// <summary>ضمن قائمة قيم مفصولة بفاصلة/سطر.</summary>
        In,
        NotIn
    }

    /// <summary>العوامل الستّة بكيان + In/NotIn (لازمة لمعيار «الموظفين» كأفراد).</summary>
    public static readonly IReadOnlyList<(Operator Op, string Label)> Operators = new[]
    {
        (Operator.Equal,          "يساوي"),
        (Operator.NotEqual,       "لا يساوي"),
        (Operator.LessThan,       "أقل من"),
        (Operator.GreaterThan,    "أكبر من"),
        (Operator.LessOrEqual,    "أقل من أو يساوي"),
        (Operator.GreaterOrEqual, "أكبر من أو يساوي"),
        (Operator.In,             "ضمن"),
        (Operator.NotIn,          "ليس ضمن")
    };

    public static string OperatorLabel(Operator op) =>
        Operators.FirstOrDefault(entry => entry.Op == op).Label ?? op.ToString();

    /// <summary>عوامل الترتيب لا معنى لها على نصّ أو منطقيّ — تُخفى بالواجهة وتُرفض بالتقييم.</summary>
    public static bool SupportsOperator(ValueKind kind, Operator op) => kind switch
    {
        ValueKind.Number or ValueKind.Date => true,
        ValueKind.Reference => op is Operator.Equal or Operator.NotEqual or Operator.In or Operator.NotIn,
        ValueKind.Text or ValueKind.Lookup => op is Operator.Equal or Operator.NotEqual or Operator.In or Operator.NotIn,
        ValueKind.Bool => op is Operator.Equal or Operator.NotEqual,
        _ => false
    };

    /// <summary>
    /// قيمة حقيقة واحدة عن موظف. كلّها فارغة ⟹ <see cref="IsMissing"/> ⟹ لا تطابق.
    /// نوع محدّد بدل <c>object</c> حتى لا تُقارَن السلاسل رقمياً بالخطأ.
    /// </summary>
    public readonly record struct Fact(decimal? Number, string? Text, DateOnly? Date, bool? Flag)
    {
        public static readonly Fact Missing = new(null, null, null, null);

        public static Fact Of(decimal? value) => value is null ? Missing : new(value, null, null, null);
        public static Fact Of(int? value) => value is null ? Missing : new(value.Value, null, null, null);
        public static Fact Of(string? value) =>
            string.IsNullOrWhiteSpace(value) ? Missing : new(null, value.Trim(), null, null);
        public static Fact Of(DateOnly? value) => value is null ? Missing : new(null, null, value, null);
        public static Fact Of(bool? value) => value is null ? Missing : new(null, null, null, value);

        public bool IsMissing => Number is null && Text is null && Date is null && Flag is null;
    }

    // ── نموذج القاعدة ──────────────────────────────────────────────────────────

    public sealed class Rule
    {
        [JsonPropertyName("criterion")] public string Criterion { get; set; } = string.Empty;
        [JsonPropertyName("op")] public Operator Op { get; set; } = Operator.Equal;
        [JsonPropertyName("value")] public string Value { get; set; } = string.Empty;
    }

    public sealed class Group
    {
        [JsonPropertyName("rules")] public List<Rule> Rules { get; set; } = new();
    }

    public sealed class ConditionSet
    {
        [JsonPropertyName("groups")] public List<Group> Groups { get; set; } = new();

        /// <summary>لا مجموعات ⟹ «بلا شروط». معنى ذلك يقرّره المستدعي لا المحرّك.</summary>
        [JsonIgnore]
        public bool IsEmpty => Groups.Count == 0 || Groups.All(group => group.Rules.Count == 0);

        [JsonIgnore]
        public int RuleCount => Groups.Sum(group => group.Rules.Count);
    }

    // ── التقييم ────────────────────────────────────────────────────────────────

    /// <summary>
    /// هل تنطبق مجموعة الشروط على هذه الحقائق؟ AND داخل المجموعة، OR بين المجموعات.
    ///
    /// <paramref name="matchWhenEmpty"/> يحسم الحالة الفارغة صراحةً لأن معناها يختلف
    /// بحسب الاستعمال، وتركُه ضمنياً هو بالضبط نوع «الحالة المخفية» الذي أتلف أوعية
    /// الاحتساب سابقاً:
    /// <list type="bullet">
    /// <item><b>نسخة سياسة</b> ⟶ <c>false</c>: نسخة بلا شروط تُظلّل السياسة الأمّ للجميع.</item>
    /// <item><b>أهلية طلب</b> ⟶ <c>true</c>: طلب بلا شروط متاح للكل، وهو المتوقّع.</item>
    /// </list>
    /// </summary>
    public static bool Matches(
        ConditionSet? set,
        IReadOnlyDictionary<string, Fact> facts,
        bool matchWhenEmpty)
    {
        if (set is null || set.IsEmpty)
        {
            return matchWhenEmpty;
        }

        // مجموعة فارغة داخل مجموعات غير فارغة تُتجاهل، وإلا صارت OR بـ«صح» يبتلع الباقي.
        return set.Groups
            .Where(group => group.Rules.Count > 0)
            .Any(group => group.Rules.All(rule => MatchesRule(rule, facts)));
    }

    /// <summary>تقييم قاعدة واحدة. أي غموض (معيار مجهول · حقيقة مفقودة · قيمة غير قابلة للتحليل) ⟹ لا تطابق.</summary>
    public static bool MatchesRule(Rule rule, IReadOnlyDictionary<string, Fact> facts)
    {
        var criterion = HrConditionCatalog.Find(rule.Criterion);
        if (criterion is null)
        {
            return false;
        }

        if (!facts.TryGetValue(rule.Criterion, out var fact) || fact.IsMissing)
        {
            return false;
        }

        if (!SupportsOperator(criterion.Kind, rule.Op))
        {
            return false;
        }

        return criterion.Kind switch
        {
            ValueKind.Number or ValueKind.Reference => MatchNumber(fact, rule),
            ValueKind.Date => MatchDate(fact, rule),
            ValueKind.Bool => MatchBool(fact, rule),
            _ => MatchText(fact, rule)
        };
    }

    private static bool MatchNumber(Fact fact, Rule rule)
    {
        if (fact.Number is not { } actual)
        {
            return false;
        }

        if (rule.Op is Operator.In or Operator.NotIn)
        {
            var members = SplitValues(rule.Value)
                .Select(token => decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : (decimal?)null)
                .Where(parsed => parsed.HasValue)
                .Select(parsed => parsed!.Value)
                .ToList();

            // قائمة بلا قيمة صالحة ⟹ لا تطابق حتى بالنفي: القاعدة نفسها ناقصة لا الحقيقة.
            if (members.Count == 0) return false;

            var contains = members.Contains(actual);
            return rule.Op == Operator.In ? contains : !contains;
        }

        if (!decimal.TryParse(rule.Value?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var expected))
        {
            return false;
        }

        return Compare(actual.CompareTo(expected), rule.Op);
    }

    private static bool MatchDate(Fact fact, Rule rule)
    {
        if (fact.Date is not { } actual)
        {
            return false;
        }

        if (rule.Op is Operator.In or Operator.NotIn)
        {
            var members = SplitValues(rule.Value)
                .Select(TryParseDate)
                .Where(parsed => parsed.HasValue)
                .Select(parsed => parsed!.Value)
                .ToList();

            if (members.Count == 0) return false;

            var contains = members.Contains(actual);
            return rule.Op == Operator.In ? contains : !contains;
        }

        if (TryParseDate(rule.Value) is not { } expected)
        {
            return false;
        }

        return Compare(actual.CompareTo(expected), rule.Op);
    }

    private static bool MatchBool(Fact fact, Rule rule)
    {
        if (fact.Flag is not { } actual)
        {
            return false;
        }

        var expected = ParseBool(rule.Value);
        if (expected is null)
        {
            return false;
        }

        return rule.Op == Operator.Equal ? actual == expected : actual != expected;
    }

    private static bool MatchText(Fact fact, Rule rule)
    {
        if (fact.Text is not { } actual)
        {
            return false;
        }

        if (rule.Op is Operator.In or Operator.NotIn)
        {
            var members = SplitValues(rule.Value).ToList();
            if (members.Count == 0) return false;

            var contains = members.Any(member => string.Equals(member, actual, StringComparison.OrdinalIgnoreCase));
            return rule.Op == Operator.In ? contains : !contains;
        }

        var same = string.Equals(rule.Value?.Trim(), actual, StringComparison.OrdinalIgnoreCase);
        return rule.Op == Operator.Equal ? same : !same;
    }

    private static bool Compare(int comparison, Operator op) => op switch
    {
        Operator.Equal => comparison == 0,
        Operator.NotEqual => comparison != 0,
        Operator.LessThan => comparison < 0,
        Operator.GreaterThan => comparison > 0,
        Operator.LessOrEqual => comparison <= 0,
        Operator.GreaterOrEqual => comparison >= 0,
        _ => false
    };

    /// <summary>فواصل قائمة القيم — نفس فواصل لصق إكسل بـ<see cref="PayrollRunScope"/> للاتساق.</summary>
    private static readonly char[] ValueSeparators = { ',', ';', '\n', '\r', '\t' };

    public static IEnumerable<string> SplitValues(string? raw) =>
        (raw ?? string.Empty)
            .Split(ValueSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 0);

    private static DateOnly? TryParseDate(string? raw) =>
        DateOnly.TryParse(raw?.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : DateOnly.TryParse(raw?.Trim(), out var fallback) ? fallback : null;

    private static bool? ParseBool(string? raw) => (raw ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "true" or "1" or "yes" or "نعم" => true,
        "false" or "0" or "no" or "لا" => false,
        _ => null
    };

    // ── التخزين ────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// تُخزَّن المجموعة كـJSON بعمود واحد لا بجدولَي مجموعات/قواعد. السبب أنها
    /// **لا تُستعلَم أبداً بالـSQL** — تُقرأ كاملةً وتُقيَّم بالذاكرة — فجدولان
    /// يعنيان هجرتين وربطاً وحذفاً متتالياً بلا مقابل. والعمود nvarchar(max) واحد
    /// يجعل إضافة الشروط لأي ميزة قائمة **عموداً واحداً** لا مخططاً جديداً.
    /// </summary>
    public static string Serialize(ConditionSet? set) =>
        set is null || set.IsEmpty ? string.Empty : JsonSerializer.Serialize(set, JsonOptions);

    /// <summary>JSON تالف ⟹ مجموعة فارغة لا استثناء: شرطٌ لا يُقرأ يجب ألا يُسقط الصفحة.</summary>
    public static ConditionSet Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ConditionSet();
        }

        try
        {
            return JsonSerializer.Deserialize<ConditionSet>(json, JsonOptions) ?? new ConditionSet();
        }
        catch (JsonException)
        {
            return new ConditionSet();
        }
    }

    /// <summary>وصف عربي مقروء للشروط — يُعرض بصفّ القائمة فيُفهم الشرط بلا فتح النموذج.</summary>
    public static string Describe(ConditionSet? set)
    {
        if (set is null || set.IsEmpty)
        {
            return "بلا شروط";
        }

        var groups = set.Groups
            .Where(group => group.Rules.Count > 0)
            .Select(group => string.Join(" و ", group.Rules.Select(DescribeRule)));

        return string.Join(" أو ", groups.Select(text => $"({text})"));
    }

    private static string DescribeRule(Rule rule)
    {
        var criterion = HrConditionCatalog.Find(rule.Criterion);
        var name = criterion?.Label ?? rule.Criterion;
        return $"{name} {OperatorLabel(rule.Op)} {rule.Value}";
    }
}
