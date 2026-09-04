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
    /// <summary>مفتاح الإعداد بـ<c>ZynoraHrSettings</c>.</summary>
    public const string ModeKey = "Payroll.AttendanceLink";

    /// <summary>بلا بيانات حضور ⟹ يُدفع الأساسي كاملاً (مع إعلان) — السلوك القائم.</summary>
    public const string Lenient = "Lenient";

    /// <summary>بلا بيانات حضور ⟹ لا يُحتسب الموظف، ويُعدّ برسالة الاحتساب.</summary>
    public const string Strict = "Strict";

    /// <summary>
    /// التنسيب على **أيام الحضور الفعلية**: من داوَم 10 أيام يأخذ راتب 10 أيام.
    /// يختلف عن <see cref="Strict"/> بأن اليوم الناقص/غير المحسوم لا يُدفع لمجرد
    /// أنه ليس «غياباً» مسجّلاً. بلا بيانات ⟹ لا يُحتسب.
    /// </summary>
    public const string PresentDays = "PresentDays";

    /// <summary>التنسيب بالساعات الفعلية ÷ المتوقّعة بدل الأيام؛ بلا بيانات ⟹ لا يُحتسب.</summary>
    public const string Hours = "Hours";

    /// <summary>معامل خصم يوم الغياب (1 = يوم بيوم، 2 = يوم بيومين…).</summary>
    public const string AbsenceFactorKey = "Payroll.AbsenceDeductionDays";

    /// <summary>هل يُسمح لمعامل التنسيب بالنزول تحت الصفر (فيصير الراتب سالباً)؟</summary>
    public const string AllowNegativeKey = "Payroll.AllowNegativeSalary";

    /// <summary>
    /// الساعات المعيارية الافتراضية لليوم. القيمة الفعلية صارت **حقلاً على السياسة**
    /// تُحمَّل من نفس الإعداد <c>Payroll.StandardDailyHours</c>
    /// (<see cref="PayrollDivisorPolicy.StandardDailyHoursKey"/>) الذي يستعمله
    /// الأوفرتايم — مصدرٌ واحد كي لا يختلف نمطُ الحضور بالساعات عن الأوفرتايم.
    /// يبقى الثابت مصدرَ الافتراض (8) فلا ينحرف أي اختبار/سلوك قائم.
    /// </summary>
    public const decimal StandardDailyHours = 8m;

    /// <summary>
    /// قرار الموظف الواحد: هل يدخل القسيمة؟ بأي معامل؟ وبأي ملاحظة تُعرض؟
    /// الملاحظة ليست تجميلاً — هي الفرق بين «حضر كاملاً» و«لا بيانات».
    /// </summary>
    public sealed record Decision(bool Include, decimal Factor, string? Note);

    /// <summary>
    /// سياسة التنسيب كاملةً. <paramref name="AbsenceDeductionDays"/> = 1 و
    /// <paramref name="AllowNegative"/> = false يعيدان سلوك ما قبل هذا الملف حرفياً.
    /// </summary>
    // مقام التنسيب (MonthlyDivisorDays) لم يعد إعداداً منفصلاً: يأتي من سياسة الغلق
    // من نوع WorkingDays (PayrollCutoffType.WorkingDays) — «ماكو شي ثابت، كلها سياسة».
    public sealed record Policy(
        string Mode, decimal AbsenceDeductionDays, bool AllowNegative, decimal StandardDailyHours = 8m,
        // مقام التنسيب الشهري. 0 ⟹ استعمل أيام الدوام (السلوك القديم حرفياً).
        // القيمة الموجبة (30 أو أيام الفترة) تجعل أيام الراحة/العطل مدفوعةً والغياب ÷ المقام.
        int MonthlyDivisorDays = 0)
    {
        public static Policy Default { get; } = new(Lenient, 1m, false);

        public Policy Normalized() => this with
        {
            Mode = NormalizeMode(Mode),
            // معامل سالب يقلب الخصم مكافأةً — يعود ليوم-بيوم لا لصفر.
            AbsenceDeductionDays = AbsenceDeductionDays < 0m ? 1m : AbsenceDeductionDays,
            // ساعات غير موجبة تجعل القسمة على المتوقّع مستحيلة — تعود لـ8.
            StandardDailyHours = StandardDailyHours > 0m ? StandardDailyHours : 8m,
            MonthlyDivisorDays = MonthlyDivisorDays < 0 ? 0 : MonthlyDivisorDays
        };
    }

    public static string NormalizeMode(string? mode) => mode switch
    {
        Strict => Strict,
        PresentDays => PresentDays,
        Hours => Hours,
        _ => Lenient
    };

    public static string ModeLabel(string? mode) => NormalizeMode(mode) switch
    {
        Strict => "صارم — بلا بيانات حضور لا يُحتسب",
        PresentDays => "أيام الحضور — من داوَم 10 أيام يأخذ راتب 10 أيام",
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
    /// يقرّر التنسيب لموظف واحد.
    ///
    /// السقف 100% دائماً: الزائد عن الدوام **أوفرتايم ببند مستقل** لا زيادة
    /// بالأساسي. أما القاع فصفر إلا إذا سُمح بالسالب صراحةً — لأن معامل خصم
    /// الغياب أكبر من 1 (يوم بيومين نمط لوائح الجزاءات) قد يبتلع الاستحقاق
    /// كلّه فيصير الصافي سالباً، وهو **سلوك مقصود** حين يُفعَّل لا خطأ حسابي.
    /// </summary>
    public static Decision Evaluate(
        Policy policy, int workDays, int presentDays, int absentDays, decimal workedHours,
        // أيام تقويمية قبل تاريخ التعيين/إعادة التعيين ضمن الشهر — غير مدفوعة.
        // 0 ⟹ موظفٌ كامل الشهر (السلوك القديم). تُطبَّق مع المقام التقويمي فقط.
        int preEmploymentUnpaidDays = 0)
    {
        var p = policy.Normalized();

        if (!HasAttendanceData(workDays))
        {
            return p.Mode == Lenient
                ? new Decision(true, 1m, "بلا بيانات حضور لهذا الشهر — دُفع الأساسي كاملاً")
                : new Decision(false, 0m, "بلا بيانات حضور لهذا الشهر — لم يُحتسب");
        }

        // الخصم الزائد عن يوم-بيوم: (المعامل − 1) × أيام الغياب. المعامل 1 ⟹ صفر،
        // فيبقى الأساس القديم كما هو تماماً.
        var extraPenaltyDays = absentDays * (p.AbsenceDeductionDays - 1m);
        var expectedHours = workDays * p.StandardDailyHours;

        // أيام غير المدفوعة حسب النمط — مُوحَّدة كي يصير المقام قابلاً للتبديل:
        //   بالساعات   ⟶ نقص الساعات معبَّراً عنه بأيام (المتوقّع − الفعلي) ÷ ساعات اليوم
        //   أيام الحضور ⟶ أيام الدوام التي لم يُحضَر فيها
        //   متساهل/صارم ⟶ أيام الغياب المسجّلة
        decimal unpaidDays;
        string? note;
        if (p.Mode == Hours)
        {
            unpaidDays = workDays - (expectedHours > 0m ? workedHours / p.StandardDailyHours : 0m);
            note = $"تنسيب بالساعات: {workedHours:0.##} من {expectedHours:0.##} ساعة";
        }
        else if (p.Mode == PresentDays)
        {
            unpaidDays = workDays - presentDays;
            note = $"تنسيب بأيام الحضور: {presentDays} من {workDays} يوم";
        }
        else
        {
            unpaidDays = absentDays;
            note = null;
        }

        // المقام: 0 ⟹ أيام الدوام (السلوك القديم حرفياً)؛ قيمةٌ موجبة ⟹ ثابت 30 أو
        // أيام الفترة — فأيام الراحة/العطل تصير مدفوعة والغياب يُخصم بنسبتها للمقام.
        var divisor = p.MonthlyDivisorDays > 0 ? (decimal)p.MonthlyDivisorDays : workDays;
        // preEmploymentUnpaidDays موجب ⟹ أيام قبل التعيين غير مدفوعة (المُعيَّن يوم 5 ⟹
        // 4 أيام ⟹ 26/30). سالب ⟹ **أثر رجعي**: أيام دورةٍ مُرحَّلة تُدفع الآن، فالمعامل
        // يتجاوز 1 (مثلاً 26/7 مُرحَّل ⟹ سبتمبر يدفع يوليو المتبقّي + أغسطس).
        var factor = 1m - ((unpaidDays + extraPenaltyDays + preEmploymentUnpaidDays) / divisor);

        if (preEmploymentUnpaidDays > 0)
            note = (note == null ? "" : note + " · ") +
                   $"تنسيب تعيين: {preEmploymentUnpaidDays} يوم قبل المباشرة غير مدفوع";
        else if (preEmploymentUnpaidDays < 0)
            note = (note == null ? "" : note + " · ") +
                   $"أثر رجعي (تعيين مُرحَّل): {-preEmploymentUnpaidDays} يوم إضافيّة";

        if (extraPenaltyDays > 0m)
            note = (note == null ? "" : note + " · ") +
                   $"خصم غياب مضاعف: {absentDays} يوم × {p.AbsenceDeductionDays:0.##}";

        // السقف 1 يُرفع فقط للأثر الرجعي (preEmploymentUnpaidDays < 0)، وإلا يبقى ≤ 1.
        return new Decision(true, Clamp(factor, p.AllowNegative, allowAboveOne: preEmploymentUnpaidDays < 0), note);
    }

    private static decimal Clamp(decimal value, bool allowNegative, bool allowAboveOne = false) =>
        value > 1m && !allowAboveOne ? 1m : value < 0m && !allowNegative ? 0m : value;
}
