using System.Globalization;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// وعاء استحقاقٍ شهريّ (للأوفرتايم/الإجازة غير المدفوعة) يُركَّب من الأساسي وحده
/// (السلوك القائم) أو الأساسي + علاوات مؤهَّلة حين تختار المنظّمة ذلك.
///
/// <para>⚠️ التوافق الخلفيّ: النمط الافتراضي <see cref="ModeBasic"/> يعيد «الأساسي»
/// حرفياً — فالأجر اليوميّ/الساعيّ يبقى <c>الأساسي ÷ 30 ÷ 8</c> كما كان.</para>
/// </summary>
public static class PayrollEarningBase
{
    /// <summary>الوعاء = الأساسي وحده (السلوك القائم).</summary>
    public const string ModeBasic = "Basic";

    /// <summary>الوعاء = الأساسي + العلاوات المؤهَّلة لهذا الغرض.</summary>
    public const string ModeBasicPlusAllowances = "BasicPlusAllowances";

    public static string NormalizeMode(string? mode) =>
        string.Equals(mode?.Trim(), ModeBasicPlusAllowances, StringComparison.OrdinalIgnoreCase)
            ? ModeBasicPlusAllowances
            : ModeBasic;

    /// <summary>
    /// يركّب الوعاء. <paramref name="eligibleAllowances"/> = مجموع العلاوات المؤهَّلة
    /// <b>بمبالغها الكاملة</b> (قبل أثر الحضور) — الأجر اليوميّ مرجعُه الراتب الشهريّ.
    /// </summary>
    public static decimal Compose(string? mode, decimal basic, decimal eligibleAllowances) =>
        NormalizeMode(mode) == ModeBasicPlusAllowances ? basic + eligibleAllowances : basic;
}

/// <summary>
/// اشتقاق الأجر اليوميّ/الساعيّ من وعاءٍ شهريّ ومقامٍ صريح — بديلٌ للثابتين
/// المبعثرين <c>÷ 30</c> و<c>÷ 8</c>. التقريب (4 خانات لكلٍّ) مطابقٌ للمحرك القائم.
/// </summary>
public static class PayrollRateBasis
{
    public static decimal DailyRate(decimal monthlyBase, decimal divisor) =>
        divisor > 0m && monthlyBase != 0m ? decimal.Round(monthlyBase / divisor, 4) : 0m;

    public static decimal HourlyRate(decimal dailyRate, decimal dailyHours) =>
        dailyHours > 0m && dailyRate != 0m ? decimal.Round(dailyRate / dailyHours, 4) : 0m;
}

/// <summary>
/// سياسة مقام أيام الراتب والساعات المعيارية — إعدادٌ صريح بدل حسابٍ مخفيّ.
///
/// <para>⚠️ الافتراض <see cref="BasisFixed30"/> و<see cref="DefaultDailyHours"/> = 8
/// يعيدان <c>÷ 30</c> و<c>÷ 8</c> حرفياً، فلا تتغيّر قسيمة قائمة.</para>
/// </summary>
public static class PayrollDivisorPolicy
{
    public const string SalaryDaysBasisKey = "Payroll.SalaryDaysBasis";
    public const string StandardDailyHoursKey = "Payroll.StandardDailyHours";

    /// <summary>مقام ثابت 30 (نمط الرواتب الخليجيّ) — الافتراض.</summary>
    public const string BasisFixed30 = "Fixed30";

    /// <summary>مقام = أيام الفترة الفعلية (28/29/30/31).</summary>
    public const string BasisPeriodDays = "PeriodDays";

    public const decimal DefaultDailyHours = 8m;

    public static string NormalizeBasis(string? basis) =>
        string.Equals(basis?.Trim(), BasisPeriodDays, StringComparison.OrdinalIgnoreCase)
            ? BasisPeriodDays
            : BasisFixed30;

    /// <summary>المقام الفعّال: 30 للثابت، وأيام الفترة لنمط أيام الفترة.</summary>
    public static decimal Divisor(string? basis, int daysInPeriod) =>
        NormalizeBasis(basis) == BasisPeriodDays && daysInPeriod > 0 ? daysInPeriod : 30m;

    /// <summary>الساعات المعيارية لليوم من الإعداد؛ القيمة التالفة/غير الموجبة ⟹ 8.</summary>
    public static decimal DailyHours(string? raw) =>
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var h) && h > 0m
            ? h
            : DefaultDailyHours;
}
