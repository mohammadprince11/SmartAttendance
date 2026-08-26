using System.Globalization;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// تسوية الإنهاء وبنود «الفروقات» (نظير شاشة إنهاء الموظف بكيان) — منطق نقيّ.
///
/// <b>الفجوة التي يسدّها:</b> عندنا **حساب مكافأة نهاية خدمة**، وعندهم **تسوية
/// إنهاء**. الفرق أن الاقتطاعات الشهرية (ضريبة · ضمان) تُحسب شهراً بشهر بافتراض
/// أن الموظف سيكمل السنة؛ فإذا خرج بمنتصفها اختلّ الأساس السنوي — فيخرج **بمبلغ
/// خاطئ زيادةً أو نقصاً**. وهذا خطأ مالي حقيقي لا نقص ميزة.
///
/// ⚠️⚠️ <b>هذا الملف لا يغيّر أي صيغة قائمة ولا يمسّ حساب نهاية الخدمة.</b>
/// يحسب **فرقاً** بين ما اقتُطع فعلاً وما كان مستحقّاً، ويعرضه بنداً مستقلاً
/// يُعتمد صراحةً. بلا اعتماد صريح لا يتغيّر مبلغ التسوية بفلس.
/// </summary>
public static class TerminationSettlementPolicy
{
    /// <summary>بنود الفروقات — أسماء ثابتة فلا تتناثر النصوص بالشاشات.</summary>
    public const string ItemTax = "Tax";
    public const string ItemGosi = "Gosi";
    public const string ItemInsurance = "Insurance";
    public const string ItemSavingsFund = "SavingsFund";
    public const string ItemContractEnd = "ContractEnd";
    public const string ItemOther = "Other";

    public static string ItemLabel(string? item) => item switch
    {
        ItemTax => "فرق ضريبة الدخل",
        ItemGosi => "فرق الضمان الاجتماعي",
        ItemInsurance => "فرق التأمين الصحي",
        ItemSavingsFund => "فرق صندوق الادخار",
        ItemContractEnd => "بدل نهاية العقد",
        _ => "فرق آخر"
    };

    /// <summary>
    /// بند فرق واحد. <see cref="Amount"/> **موجبٌ دائماً** والاتجاه بـ<see cref="IsRefund"/>
    /// — مبلغٌ سالب يُعرض بعلامتين ويُقرأ خطأً بكل شاشة تعرضه.
    /// </summary>
    public sealed record Difference(string Item, decimal Withheld, decimal Due)
    {
        /// <summary>اقتُطع أكثر ممّا استُحقّ ⟹ يُردّ للموظف.</summary>
        public bool IsRefund => Withheld > Due;

        /// <summary>
        /// ⚠️ <c>AwayFromZero</c> لا التقريب المصرفي الافتراضي: نصفُ فلسٍ بمبلغ
        /// يُسلَّم للموظف يجب أن يميل لصالحه لا أن يُقرَّب للزوجي — والتقريب
        /// المصرفي كان يعطي 100.00 حيث الصحيح 100.01.
        /// </summary>
        public decimal Amount => Math.Abs(Math.Round(Withheld - Due, 2, MidpointRounding.AwayFromZero));

        public string Label => ItemLabel(Item);

        /// <summary>أثره على صافي التسوية: موجبٌ إن رُدّ للموظف، سالبٌ إن استُحقّ عليه.</summary>
        public decimal SignedAmount => IsRefund ? Amount : -Amount;

        public string DirectionLabel => Amount == 0 ? "لا فرق" : IsRefund ? "يُردّ للموظف" : "يُستحقّ على الموظف";
    }

    /// <summary>
    /// صافي الفروقات. **البنود صفرية الفرق تُستبعَد** لا تُجمع بصفر: عرضها يشوّش،
    /// وحسابها ضمن الصافي لا أثر له.
    /// </summary>
    public static decimal NetDifference(IEnumerable<Difference> differences) =>
        Math.Round(differences.Where(d => d.Amount != 0).Sum(d => d.SignedAmount), 2, MidpointRounding.AwayFromZero);

    public static List<Difference> Material(IEnumerable<Difference> differences) =>
        differences.Where(d => d.Amount != 0).ToList();

    /// <summary>
    /// ⭐ <b>التنبيه الأرخص والأكثر منعاً للخطأ</b> (توصية الدراسة): هل خرج الموظف
    /// بشهرٍ لم يُحتسب راتبه بعد؟ شرطٌ واحد يمنع تسليم تسويةٍ ناقصة راتب شهر كامل.
    ///
    /// <paramref name="lastPaidMonth"/> = آخر شهر مسيَّر (سنة×12+شهر)، و<c>null</c>
    /// يعني «لا مسير أصلاً» وهو أيضاً حالةُ تنبيه لا حالةُ اطمئنان.
    /// </summary>
    public static bool TerminationMonthUnpaid(DateOnly terminationDate, int? lastPaidMonthKey)
    {
        var terminationKey = (terminationDate.Year * 12) + terminationDate.Month;
        return lastPaidMonthKey is not { } paid || paid < terminationKey;
    }

    public static int MonthKey(int year, int month) => (year * 12) + month;

    // ── المبلغ بالحروف ─────────────────────────────────────────────────────────

    private static readonly string[] Ones =
    {
        "", "واحد", "اثنان", "ثلاثة", "أربعة", "خمسة", "ستة", "سبعة", "ثمانية", "تسعة",
        "عشرة", "أحد عشر", "اثنا عشر", "ثلاثة عشر", "أربعة عشر", "خمسة عشر",
        "ستة عشر", "سبعة عشر", "ثمانية عشر", "تسعة عشر"
    };

    private static readonly string[] Tens =
    {
        "", "", "عشرون", "ثلاثون", "أربعون", "خمسون", "ستون", "سبعون", "ثمانون", "تسعون"
    };

    private static readonly string[] Hundreds =
    {
        "", "مئة", "مئتان", "ثلاثمئة", "أربعمئة", "خمسمئة", "ستمئة", "سبعمئة", "ثمانمئة", "تسعمئة"
    };

    /// <summary>
    /// المبلغ بالحروف — بندٌ رسمي بكل تسوية إنهاء (وثيقة تُوقَّع، والرقم وحده
    /// قابل للتحريف بقلم). يدعم حتى المليارات وبكسورٍ من خانتين.
    /// </summary>
    public static string AmountInWords(decimal amount, string currency = "دينار", string fraction = "فلس")
    {
        var negative = amount < 0;
        amount = Math.Abs(Math.Round(amount, 2));

        var whole = (long)decimal.Truncate(amount);
        var cents = (int)Math.Round((amount - whole) * 100, 0);

        var text = whole == 0 ? "صفر" : ThreeGroups(whole);
        var result = $"{text} {currency}";

        if (cents > 0)
        {
            result += $" و{ThreeGroups(cents)} {fraction}";
        }

        return negative ? $"سالب {result}" : result;
    }

    /// <summary>تفقيط مبلغ القسيمة باسم العملة الفعلي بدلاً من اسم ثابت.</summary>
    public static string CurrencyAmountInWords(decimal amount, string? currencyCode)
    {
        var (currency, fraction) = (currencyCode ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "IQD" => ("دينار عراقي", "فلس"),
            "JOD" => ("دينار أردني", "قرش"),
            "SAR" => ("ريال سعودي", "هللة"),
            "AED" => ("درهم إماراتي", "فلس"),
            "USD" => ("دولار أمريكي", "سنت"),
            "EUR" => ("يورو", "سنت"),
            "GBP" => ("جنيه إسترليني", "بنس"),
            { Length: > 0 } code => (code, "جزء"),
            _ => ("وحدة نقدية", "جزء")
        };

        return AmountInWords(amount, currency, fraction);
    }

    private static string ThreeGroups(long number)
    {
        if (number == 0) return string.Empty;

        var parts = new List<string>();

        var scales = new (long Value, string One, string Two, string Plural)[]
        {
            (1_000_000_000, "مليار", "ملياران", "مليارات"),
            (1_000_000, "مليون", "مليونان", "ملايين"),
            (1_000, "ألف", "ألفان", "آلاف")
        };

        foreach (var (value, one, two, plural) in scales)
        {
            var count = number / value;
            if (count == 0) continue;

            number %= value;

            parts.Add(count switch
            {
                1 => one,
                2 => two,
                >= 3 and <= 10 => $"{UnderThousand((int)count)} {plural}",
                _ => $"{UnderThousand((int)count)} {one}"
            });
        }

        if (number > 0)
        {
            parts.Add(UnderThousand((int)number));
        }

        return string.Join(" و", parts);
    }

    private static string UnderThousand(int number)
    {
        if (number == 0) return string.Empty;

        var parts = new List<string>();

        var hundreds = number / 100;
        if (hundreds > 0) parts.Add(Hundreds[hundreds]);

        var rest = number % 100;

        if (rest > 0)
        {
            if (rest < 20)
            {
                parts.Add(Ones[rest]);
            }
            else
            {
                var unit = rest % 10;
                var ten = rest / 10;

                // العربية تقدّم الآحاد على العشرات: «خمسة وعشرون» لا «عشرون وخمسة».
                parts.Add(unit > 0 ? $"{Ones[unit]} و{Tens[ten]}" : Tens[ten]);
            }
        }

        return string.Join(" و", parts);
    }

    /// <summary>مدة الخدمة بصيغة سنة/شهر/يوم — بند رسمي بوثيقة التسوية.</summary>
    public static string ServiceLength(DateOnly from, DateOnly to)
    {
        if (to < from) return "0";

        var years = to.Year - from.Year;
        var months = to.Month - from.Month;
        var days = to.Day - from.Day;

        if (days < 0)
        {
            months--;
            days += DateTime.DaysInMonth(to.AddMonths(-1).Year, to.AddMonths(-1).Month);
        }

        if (months < 0)
        {
            years--;
            months += 12;
        }

        var parts = new List<string>();
        if (years > 0) parts.Add($"{years} سنة");
        if (months > 0) parts.Add($"{months} شهر");
        if (days > 0) parts.Add($"{days} يوم");

        return parts.Count == 0 ? "0 يوم" : string.Join(" و", parts);
    }

    public static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);
}
