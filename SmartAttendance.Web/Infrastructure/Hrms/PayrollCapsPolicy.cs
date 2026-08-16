namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// الحدود القصوى الشهرية — نظير «التحقق من المبالغ القصوى» بكيان
/// (الحد الأعلى للاقتطاع الشهري · الحد الأعلى للعمل الإضافي).
///
/// **معطّلة افتراضياً** (0 = بلا سقف) كي لا تمسّ الصيغ القائمة — قاعدة الرواتب
/// الحمراء. حين تُفعَّل: تُطبَّق على **الاقتطاعات الاختيارية** (جزاءات/حركات/أيام/
/// إجازة بلا راتب/صيغ) لا على الضريبة والضمان القانونيين، وعلى مبلغ الإضافي المحتسب.
///
/// السقف يُعبَّر إما بمبلغ مطلق أو **نسبة من الإجمالي** — والأضيق يفوز.
/// المنطق نقيّ قابل للاختبار؛ القراءة من الإعدادات بالمخزن.
/// </summary>
public static class PayrollCapsPolicy
{
    public const string KeyDeductionCapAmount = "Payroll.Caps.Deduction.Amount";
    public const string KeyDeductionCapPercent = "Payroll.Caps.Deduction.PercentOfGross";
    public const string KeyOvertimeCapAmount = "Payroll.Caps.Overtime.Amount";
    public const string KeyOvertimeCapHours = "Payroll.Caps.Overtime.Hours";

    public sealed record Caps(
        decimal DeductionCapAmount,
        decimal DeductionCapPercentOfGross,
        decimal OvertimeCapAmount,
        decimal OvertimeCapHours)
    {
        public static readonly Caps None = new(0, 0, 0, 0);
        public bool HasDeductionCap => DeductionCapAmount > 0 || DeductionCapPercentOfGross > 0;
        public bool HasOvertimeCap => OvertimeCapAmount > 0 || OvertimeCapHours > 0;
    }

    /// <summary>
    /// السقف الفعلي للاقتطاعات الاختيارية لهذا الإجمالي — الأضيق بين المبلغ والنسبة.
    /// null = بلا سقف.
    /// </summary>
    public static decimal? EffectiveDeductionCap(Caps caps, decimal gross)
    {
        if (!caps.HasDeductionCap) return null;

        decimal? byAmount = caps.DeductionCapAmount > 0 ? caps.DeductionCapAmount : null;
        decimal? byPercent = caps.DeductionCapPercentOfGross > 0
            ? Math.Round(gross * caps.DeductionCapPercentOfGross / 100m, 2)
            : null;

        return (byAmount, byPercent) switch
        {
            (null, null) => null,
            (not null, null) => byAmount,
            (null, not null) => byPercent,
            _ => Math.Min(byAmount!.Value, byPercent!.Value)
        };
    }

    /// <summary>
    /// يطبّق سقف الاقتطاع: يعيد (المبلغ بعد السقف، المبلغ المُرحَّل الذي تجاوز السقف).
    /// المُرحَّل يُعلَن لا يُبتلَع — يُعرض بالقسيمة/الملاحظة كي يُحصَّل بشهر لاحق.
    /// </summary>
    public static (decimal Applied, decimal Deferred) ApplyDeductionCap(Caps caps, decimal gross, decimal optionalDeductions)
    {
        var cap = EffectiveDeductionCap(caps, gross);
        if (cap is null || optionalDeductions <= cap.Value)
            return (optionalDeductions, 0);

        return (cap.Value, Math.Round(optionalDeductions - cap.Value, 2));
    }

    /// <summary>
    /// يطبّق سقف الإضافي على الساعات ثم المبلغ (الأضيق يفوز). يعيد (ساعات مطبّقة، مبلغ مطبّق، ساعات مقصوصة).
    /// معدّل الساعة يُشتق من المدخل نفسه كي لا يُعاد احتساب شيء بصيغة أخرى.
    /// </summary>
    public static (decimal Hours, decimal Amount, decimal TrimmedHours) ApplyOvertimeCap(Caps caps, decimal hours, decimal amount)
    {
        if (!caps.HasOvertimeCap || hours <= 0 || amount <= 0)
            return (hours, amount, 0);

        var hourlyRate = amount / hours;
        var appliedHours = hours;
        var appliedAmount = amount;

        if (caps.OvertimeCapHours > 0 && appliedHours > caps.OvertimeCapHours)
        {
            appliedHours = caps.OvertimeCapHours;
            appliedAmount = Math.Round(appliedHours * hourlyRate, 2);
        }

        if (caps.OvertimeCapAmount > 0 && appliedAmount > caps.OvertimeCapAmount)
        {
            appliedAmount = caps.OvertimeCapAmount;
            appliedHours = Math.Round(appliedAmount / hourlyRate, 2);
        }

        return (appliedHours, appliedAmount, Math.Round(hours - appliedHours, 2));
    }

    /// <summary>يفكّ القيم النصية المخزّنة بأمان — أي قيمة فاسدة أو سالبة = 0 (بلا سقف).</summary>
    public static Caps Parse(string? deductionAmount, string? deductionPercent, string? overtimeAmount, string? overtimeHours)
    {
        static decimal P(string? s) => decimal.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 0m;

        return new Caps(P(deductionAmount), P(deductionPercent), P(overtimeAmount), P(overtimeHours));
    }
}
