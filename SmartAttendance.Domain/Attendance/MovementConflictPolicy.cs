namespace SmartAttendance.Domain.Attendance;

/// <summary>
/// عقد رصد «التعارض مع الحركة» لأنواع المناوبات (تبويب «التعارض» بـ<c>/ShiftTypes</c>) —
/// منطق نقي بلا قاعدة بيانات ولا مسير، هو الشريحة الأولى الآمنة من توصيل التبويب الصمّاء
/// إلى المحرك. يقارن نافذة المغادرة المعتمدة (من <c>SelfServiceRequests</c>) بالحركة الفعلية:
/// <list type="bullet">
///   <item><b>الخروج المبكّر:</b> الموظف غادر قبل بداية نافذة المغادرة المعتمدة.</item>
///   <item><b>العودة المتأخّرة:</b> الموظف عاد بعد نهاية نافذة المغادرة المعتمدة.</item>
/// </list>
/// عند وقوع تعارض تُطبَّق العقوبة المُعدّة مسبقاً في المناوبة (قيمة ثابتة): إمّا اقتطاع
/// إذن (<see cref="ActionPermission"/>) بالساعات، أو اقتطاع مبلغ (<see cref="ActionDeduction"/>).
/// القيمة ثابتة من الإعداد ولا تُشتقّ من مقدار التجاوز — الأخير معلوماتي فقط (لنصّ الحركة).
/// كل قاعدة محروسة براية <c>Enabled</c> (الافتراضي false) فلا أثر حتى يُفعّلها الأدمن.
/// مصدر الحقيقة الوحيد للرصد — يستهلكه محرك الحضور (طبقة الإصدار عبر الاقتراحات لاحقاً).
/// </summary>
public static class MovementConflictPolicy
{
    /// <summary>اقتطاع إذن بالساعات (يطابق <c>ActionType="Permission"</c> بحركات الحضور).</summary>
    public const string ActionPermission = "Permission";

    /// <summary>اقتطاع مبلغ (يطابق <c>ActionType="Deduction"</c> بحركات الحضور).</summary>
    public const string ActionDeduction = "Deduction";

    /// <summary>
    /// إعداد قاعدة تعارض واحدة كما ضبطها الأدمن في المناوبة.
    /// <paramref name="Action"/> = <see cref="ActionPermission"/> أو <see cref="ActionDeduction"/>؛
    /// <paramref name="Value"/> = ساعات (للإذن) أو مبلغ (للاقتطاع).
    /// </summary>
    public sealed record Rule(bool Enabled, string Action, decimal Value);

    /// <summary>
    /// نتيجة الرصد. <see cref="Triggered"/>=false يعني لا تعارض (قاعدة معطّلة أو لا تجاوز).
    /// عند التفعيل: <see cref="Action"/>/<see cref="Value"/> = العقوبة المُعدّة، و
    /// <see cref="MinutesOff"/> = دقائق التجاوز (معلوماتي لنصّ الحركة، لا يدخل الحساب المالي).
    /// </summary>
    public sealed record Result(bool Triggered, string Action, decimal Value, int MinutesOff)
    {
        public static readonly Result None = new(false, string.Empty, 0m, 0);
    }

    /// <summary>
    /// رصد الخروج المبكّر للمغادرة: يقع حين يغادر الموظف فعلياً <b>قبل</b> بداية نافذة
    /// المغادرة المعتمدة. لا تعارض إن كانت القاعدة معطّلة أو خرج في الوقت/بعده.
    /// </summary>
    public static Result EvaluateEarlyLeave(Rule rule, TimeOnly approvedStart, TimeOnly actualLeft)
    {
        if (!rule.Enabled || actualLeft >= approvedStart) return Result.None;

        var minutes = (int)Math.Round((approvedStart - actualLeft).TotalMinutes);
        return minutes <= 0 ? Result.None : new Result(true, rule.Action, rule.Value, minutes);
    }

    /// <summary>
    /// رصد العودة المتأخّرة من المغادرة: يقع حين يعود الموظف فعلياً <b>بعد</b> نهاية نافذة
    /// المغادرة المعتمدة. لا تعارض إن كانت القاعدة معطّلة أو عاد في الوقت/قبله.
    /// </summary>
    public static Result EvaluateLateReturn(Rule rule, TimeOnly approvedEnd, TimeOnly actualReturn)
    {
        if (!rule.Enabled || actualReturn <= approvedEnd) return Result.None;

        var minutes = (int)Math.Round((actualReturn - approvedEnd).TotalMinutes);
        return minutes <= 0 ? Result.None : new Result(true, rule.Action, rule.Value, minutes);
    }
}
