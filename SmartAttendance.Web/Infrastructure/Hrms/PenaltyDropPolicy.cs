namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// سياسة **إسقاط** الإجراء التأديبي — متى يسقط من سجلّ الموظف.
///
/// كان عندنا رقم أشهر واحد (<c>ValidityMonths</c>). وكيان تُظهر ثلاثة أبعاد:
/// <list type="bullet">
/// <item><b>النمط</b>: مرّة واحدة أم دوريّة.</item>
/// <item><b>المدّة</b>: رقم + وحدة (أيام · شهور · سنوات).</item>
/// <item><b>المرساة</b>: من تاريخ الإجراء · نهاية السنة الماليّة · نهاية السنة التعاقدية.</item>
/// </list>
///
/// والمرساة هي المهمّة عملياً: شركاتٌ تُسقط كل المخالفات **بنهاية السنة** دفعةً
/// واحدة لا بعد سنةٍ من كل مخالفة على حدة — والفرق بين المعنيين شهورٌ بسجلّ موظف.
///
/// منطق نقيّ بلا قاعدة بيانات: التواريخ تُمرَّر ولا تُقرأ.
/// </summary>
public static class PenaltyDropPolicy
{
    public const string ModeOnce = "Once";
    public const string ModePeriodic = "Periodic";

    public const string UnitDays = "Days";
    public const string UnitMonths = "Months";
    public const string UnitYears = "Years";

    public const string AnchorActionDate = "ActionDate";
    public const string AnchorFiscalYearEnd = "FiscalYearEnd";
    public const string AnchorContractYearEnd = "ContractYearEnd";

    public static readonly IReadOnlyList<(string Key, string Label)> Modes = new[]
    {
        (ModeOnce,     "مرّة واحدة"),
        (ModePeriodic, "دوريّة")
    };

    public static readonly IReadOnlyList<(string Key, string Label)> Units = new[]
    {
        (UnitDays,   "أيام"),
        (UnitMonths, "شهور"),
        (UnitYears,  "سنوات")
    };

    public static readonly IReadOnlyList<(string Key, string Label)> Anchors = new[]
    {
        (AnchorActionDate,      "من تاريخ الإجراء"),
        (AnchorFiscalYearEnd,   "من نهاية السنة الماليّة"),
        (AnchorContractYearEnd, "من نهاية السنة التعاقدية")
    };

    public static string UnitLabel(string? key) =>
        Units.FirstOrDefault(u => string.Equals(u.Key, key, StringComparison.OrdinalIgnoreCase)).Label ?? "شهور";

    public static string AnchorLabel(string? key) =>
        Anchors.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase)).Label ?? "من تاريخ الإجراء";

    private static string NormalizeUnit(string? unit) =>
        Units.Any(u => string.Equals(u.Key, unit, StringComparison.OrdinalIgnoreCase))
            ? Units.First(u => string.Equals(u.Key, unit, StringComparison.OrdinalIgnoreCase)).Key
            : UnitMonths;

    private static string NormalizeAnchor(string? anchor) =>
        Anchors.Any(a => string.Equals(a.Key, anchor, StringComparison.OrdinalIgnoreCase))
            ? Anchors.First(a => string.Equals(a.Key, anchor, StringComparison.OrdinalIgnoreCase)).Key
            : AnchorActionDate;

    private static DateOnly Add(DateOnly from, int amount, string unit) => NormalizeUnit(unit) switch
    {
        UnitDays => from.AddDays(amount),
        UnitYears => from.AddYears(amount),
        _ => from.AddMonths(amount)
    };

    /// <summary>
    /// تاريخ سقوط الإجراء. <c>null</c> = **لا يسقط أبداً** (مدّة صفرية أو سالبة).
    ///
    /// <paramref name="fiscalYearEnd"/> و<paramref name="contractYearEnd"/> يمرّرهما
    /// المستدعي؛ غيابهما مع مرساةٍ تطلبهما يرتدّ لتاريخ الإجراء — **لا يُلغى
    /// الإسقاط بصمت**، لأن مخالفةً لا تسقط أبداً بسبب إعدادٍ ناقص خطأٌ عمّاليّ.
    /// </summary>
    public static DateOnly? DropOn(
        string? mode,
        int every,
        string? unit,
        string? anchor,
        DateOnly actionDate,
        DateOnly? fiscalYearEnd = null,
        DateOnly? contractYearEnd = null)
    {
        if (every <= 0)
        {
            return null;
        }

        var start = NormalizeAnchor(anchor) switch
        {
            AnchorFiscalYearEnd => fiscalYearEnd ?? actionDate,
            AnchorContractYearEnd => contractYearEnd ?? actionDate,
            _ => actionDate
        };

        var candidate = Add(start, every, unit);

        // الدوريّة تتقدّم حتى تتجاوز تاريخ الإجراء: مرساةٌ سابقة له (نهاية سنةٍ
        // مضت) كانت ستعطي تاريخ سقوطٍ **بالماضي** — أي مخالفةً ساقطةً يوم تسجيلها.
        if (string.Equals(mode, ModePeriodic, StringComparison.OrdinalIgnoreCase))
        {
            var guard = 0;
            while (candidate <= actionDate && guard++ < 400)
            {
                candidate = Add(candidate, every, unit);
            }
        }
        else if (candidate <= actionDate)
        {
            // «مرّة واحدة» بمرساةٍ ماضية: تُرفع لتاريخ الإجراء فلا تسقط قبل وقوعها.
            candidate = Add(actionDate, every, unit);
        }

        return candidate;
    }

    /// <summary>هل سقط الإجراء عند تاريخٍ مرجعيّ؟</summary>
    public static bool IsDropped(DateOnly? dropOn, DateOnly asOf) =>
        dropOn is { } date && asOf >= date;

    /// <summary>وصف عربي مقروء — نصّ صفّ السلّم بالشاشة.</summary>
    public static string Describe(string? mode, int every, string? unit, string? anchor)
    {
        if (every <= 0)
        {
            return "لا يسقط";
        }

        var head = string.Equals(mode, ModePeriodic, StringComparison.OrdinalIgnoreCase)
            ? "يسقط كل"
            : "يسقط بعد";

        return $"{head} {every} {UnitLabel(unit)} {AnchorLabel(anchor)}";
    }
}
