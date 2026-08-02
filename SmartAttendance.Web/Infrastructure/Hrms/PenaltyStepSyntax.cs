namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// ترميزٌ مختصر لدرجة جزاءٍ واحدة، كي تُكتب لائحةٌ من مئتَي درجة **بصفٍّ لكل
/// مخالفة** فتُقرأ كما تُقرأ الورقة الأصلية.
///
/// <code>
///   N        لفت نظر            K       تنبيه/إنذار كتابي
///   A1       إنذار أول          A2      إنذار ثانٍ/نهائي بالفصل
///   D2       خصم أجر يومين      D0.25   خصم أجر ربع يوم
///   F        فصل من الخدمة      -       لا جزاء بهذه الدرجة
///   D2+A1    خصمٌ ومعه إنذار
/// </code>
///
/// ⚠️ **الدرجة المركّبة تُحفظ بنوعٍ واحد**: نموذجنا (كنموذج كيان) يحمل نوع إجراءٍ
/// واحداً لكل درجة، والمالُ هو ما يُحتسب — فالخصم يفوز بالنوع، والإنذار يبقى
/// **بعنوان الإجراء** فيظهر بالكتاب الرسمي كما نصّت اللائحة. البديل — صفّان
/// لدرجةٍ واحدة — كان سيضاعف العقوبة عند الاحتساب.
/// </summary>
public static class PenaltyStepSyntax
{
    /// <summary>درجةٌ مفكوكة: نوعها وأثرها المالي وعنوانها العربي.</summary>
    public sealed record Step(string ActionType, string ImpactType, decimal Value, string Title)
    {
        /// <summary>الدرجة الفارغة «-» — لا جزاء بهذا التكرار.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(ActionType);

        public static readonly Step None = new(string.Empty, "None", 0m, string.Empty);
    }

    public static Step Parse(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim() == "-")
        {
            return Step.None;
        }

        var parts = code.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string action = string.Empty;
        var impact = "None";
        var value = 0m;
        var titles = new List<string>();

        foreach (var part in parts)
        {
            if (part.StartsWith("D", StringComparison.OrdinalIgnoreCase)
                && decimal.TryParse(part[1..], out var days))
            {
                // المال يفوز بالنوع دائماً — هو وحده ما يُحتسب بالقسيمة.
                action = PenaltyAction.SalaryDeduction;
                impact = "Days";
                value = days;
                titles.Add($"خصم أجر {DaysText(days)}");
                continue;
            }

            var (key, label) = part.ToUpperInvariant() switch
            {
                "N" => (PenaltyAction.VerbalWarning, "لفت نظر"),
                "K" => (PenaltyAction.WrittenWarning, "تنبيه خطّي"),
                "A1" => (PenaltyAction.Warning, "إنذار أول"),
                "A2" => (PenaltyAction.FinalWarning, "إنذار نهائي بالفصل"),
                "F" => (PenaltyAction.Termination, "الفصل من الخدمة دون عودة"),
                _ => (string.Empty, string.Empty)
            };

            if (key.Length == 0) continue;

            titles.Add(label);

            // لا يُزاح نوعٌ ماليّ سبق تحديده، ويُرقّى غير الماليّ للأشدّ.
            if (action != PenaltyAction.SalaryDeduction && Rank(key) > Rank(action))
            {
                action = key;
            }
        }

        return action.Length == 0
            ? Step.None
            : new Step(action, impact, value, string.Join(" + ", titles));
    }

    /// <summary>سلّم الشدّة — يحسم أيّ نوعٍ يمثّل درجةً مركّبة.</summary>
    private static int Rank(string? action) => action switch
    {
        PenaltyAction.VerbalWarning => 1,
        PenaltyAction.WrittenWarning => 2,
        PenaltyAction.Warning => 3,
        PenaltyAction.FinalWarning => 4,
        PenaltyAction.RaiseDenial => 5,
        PenaltyAction.SalaryDeduction => 6,
        PenaltyAction.Termination => 7,
        _ => 0
    };

    /// <summary>«½ يوم» لا «0.5 يوم» — اللائحة الأصلية تكتبها كسوراً عربية.</summary>
    public static string DaysText(decimal days) => days switch
    {
        0.25m => "¼ يوم",
        0.5m => "½ يوم",
        0.75m => "¾ يوم",
        1m => "يوم",
        2m => "يومين",
        _ => $"{days:0.##} أيام"
    };
}
