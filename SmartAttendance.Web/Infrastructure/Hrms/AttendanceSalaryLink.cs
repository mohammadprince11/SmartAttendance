namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// سياسة ربط الراتب بالحضور — دالة نقيّة تقرّر لكل موظف: هل يُحتسب أصلاً؟ وبأي
/// معامل يُنسَّب أساسيه؟
///
/// السلسلة القائمة: بصمات ⟵ <c>DayAttendances</c> المحلَّلة ⟵ الاعتماد الشهري
/// <c>EmployeeMonthAttendance</c> ⟵ معامل التنسيب هنا. الخلل الذي كشفه محمد
/// (2026-07-31) أن **غياب صفّ الاعتماد كان يعني معامل 1 بصمت** — قسيمة تقول
/// «أيام العمل 0 / 0» وتدفع الأساسي كاملاً بلا أي تمييز بين «داوَم شهراً كاملاً»
/// و«لا بيانات حضور إطلاقاً». الصمت هو العيب، لا الافتراض نفسه.
///
/// فصار السلوك **سياسة يختارها المستخدم** (<see cref="ModeKey"/>) بثلاثة أنماط،
/// والحالة تُعلَن دائماً بالقسيمة وبرسالة الاحتساب مهما كان النمط.
///
/// ⚠️ النمط الافتراضي <see cref="Lenient"/> يعيد أرقام ما قبل هذا الملف حرفياً —
/// أي تحوّل بالافتراضي يغيّر كل قسيمة بأثر رجعي. مثبَّت باختبارات انحدار.
/// </summary>
public static class AttendanceSalaryLink
{
    /// <summary>مفتاح الإعداد بـ<c>NexoraHrSettings</c>.</summary>
    public const string ModeKey = "Payroll.AttendanceLink";

    /// <summary>بلا بيانات حضور ⟹ يُدفع الأساسي كاملاً (مع إعلان) — السلوك القائم.</summary>
    public const string Lenient = "Lenient";

    /// <summary>بلا بيانات حضور ⟹ لا يُحتسب الموظف، ويُعدّ برسالة الاحتساب.</summary>
    public const string Strict = "Strict";

    /// <summary>التنسيب بالساعات الفعلية ÷ المتوقّعة بدل الأيام؛ بلا بيانات ⟹ لا يُحتسب.</summary>
    public const string Hours = "Hours";

    /// <summary>
    /// الساعات المعيارية لليوم. نفس الرقم المستعمل باشتقاق الأجر الساعي
    /// (<c>يومي ÷ 8</c>) بـ<see cref="PayrollRunStore"/> — لو صار قابلاً للتهيئة
    /// يوماً ما فليتغيّر بالموضعين معاً وإلا اختلف الأوفرتايم عن التنسيب.
    /// </summary>
    public const decimal StandardDailyHours = 8m;

    /// <summary>
    /// قرار الموظف الواحد: هل يدخل القسيمة؟ بأي معامل؟ وبأي ملاحظة تُعرض؟
    /// الملاحظة ليست تجميلاً — هي الفرق بين «حضر كاملاً» و«لا بيانات».
    /// </summary>
    public sealed record Decision(bool Include, decimal Factor, string? Note);

    public static string NormalizeMode(string? mode) => mode switch
    {
        Strict => Strict,
        Hours => Hours,
        _ => Lenient
    };

    public static string ModeLabel(string? mode) => NormalizeMode(mode) switch
    {
        Strict => "صارم — بلا بيانات حضور لا يُحتسب",
        Hours => "بالساعات — التنسيب بساعات العمل الفعلية",
        _ => "متساهل — بلا بيانات حضور يُدفع كاملاً (مع إعلان)"
    };

    /// <summary>
    /// «بيانات الحضور موجودة» = صفّ اعتماد شهري بأيام عمل موجبة. صفٌّ بأيام صفر
    /// (موظف بلا مناوبة مسنَدة مثلاً) يُعامَل معاملة الغياب التام للبيانات: القسمة
    /// عليه مستحيلة، والادّعاء بأنه «صفر حضور» يصفّر راتباً بلا سند.
    /// </summary>
    public static bool HasAttendanceData(int workDays) => workDays > 0;

    /// <summary>
    /// يقرّر التنسيب. <paramref name="workedHours"/> تُستعمل بنمط الساعات فقط.
    /// المعامل مقصوص إلى [0, 1]: الزائد عن الدوام أوفرتايم ببند مستقل لا زيادة
    /// بالأساسي، والسالب (غياب أكثر من أيام العمل) لا معنى له.
    /// </summary>
    public static Decision Evaluate(string? mode, int workDays, int absentDays, decimal workedHours)
    {
        var resolved = NormalizeMode(mode);

        if (!HasAttendanceData(workDays))
        {
            return resolved == Lenient
                ? new Decision(true, 1m, "بلا بيانات حضور لهذا الشهر — دُفع الأساسي كاملاً")
                : new Decision(false, 0m, "بلا بيانات حضور لهذا الشهر — لم يُحتسب");
        }

        if (resolved == Hours)
        {
            var expected = workDays * StandardDailyHours;
            var factor = Clamp(workedHours / expected);
            return new Decision(true, factor,
                $"تنسيب بالساعات: {workedHours:0.##} من {expected:0.##} ساعة");
        }

        var byDays = Clamp((decimal)(workDays - absentDays) / workDays);
        return new Decision(true, byDays, null);
    }

    private static decimal Clamp(decimal value) => value < 0m ? 0m : value > 1m ? 1m : value;
}
