namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// فضاء متغيّرات صيغ الرواتب — <b>مصدر الحقيقة الوحيد</b>.
///
/// نظير فضاء كيان (`الراتب الأساسي · العلاوات · الراتب المستحق · أيام العمل
/// الفعلية · مجموع الدخل · صافي الراتب …`) الذي يغذّي منشئ الصيغة بكل شاشة مالية.
///
/// <b>لماذا صنف مستقلّ:</b> كان الكتالوج المعروض بالواجهة في مكان
/// (<c>SalaryItemStore.FormulaVars</c>)، والقاموس المُمرَّر وقت الاحتساب يُبنى
/// حرفياً بمكانين: <c>PayrollRunStore</c> و<c>Pages/HrSettings/Formulas</c> —
/// وقد <b>افترقا فعلياً</b>: المختبر كان يمرّر <c>Days=30, Hours=8</c> والمسير
/// يمرّر <c>Days=0, Hours=0</c>. فصيغةٌ تُجرَّب فتنجح، ثم تُنتج بالمسير صفراً
/// أو تفشل بقسمة على صفر فيُتخطّى بندها <b>بصمت</b>.
///
/// بعد اليوم: الكتالوج والبنّاء هنا، ولا يبني أحدٌ القاموس بيده.
/// </summary>
public static class PayrollFormulaVariables
{
    /// <summary>
    /// الكتالوج المعروض بالواجهة — نفس المفاتيح التي يقبلها المُقيِّم حرفياً.
    /// الترتيب مقصود: الأساس ← الزمن ← المعدّلات ← الحركات ← الإجماليات.
    /// </summary>
    public static readonly (string Key, string Label, string Hint)[] Catalog =
    {
        // الأساس
        ("Basic", "الراتب الأساسي", "الأساسي الكامل من الملف المالي — قبل التنسيب بالحضور"),
        ("ProratedBasic", "الراتب المستحق", "الأساسي بعد ضربه بمعامل الحضور"),
        ("Allowances", "إجمالي العلاوات", "مجموع علاوات الموظف السارية بالفترة"),
        ("Gross", "الراتب الإجمالي", "الأساسي + العلاوات (قبل الحركات)"),

        // الزمن
        ("Days", "أيام العمل المقرّرة", "أيام الدوام بالاعتماد الشهري"),
        ("PresentDays", "أيام الحضور الفعلية", "الأيام المحتسبة حضوراً أو تأخيراً"),
        ("AbsentDays", "أيام الغياب", "من الاعتماد الشهري"),
        ("UnpaidLeaveDays", "أيام الإجازة بلا راتب", "من الاعتماد الشهري"),
        ("DaysInPeriod", "عدد أيام الفترة", "أيام التقويم بين بداية الفترة ونهايتها"),
        ("NonWorkDays", "الأيام غير التشغيلية", "أيام الفترة ناقص أيام العمل المقرّرة"),
        ("Hours", "الساعات المشتغَلة", "مجموع ساعات العمل بالاعتماد الشهري"),

        // المعدّلات
        ("DailyRate", "الأجر اليومي", "الأساسي ÷ 30"),
        ("HourlyRate", "الأجر الساعي", "الأجر اليومي ÷ 8"),
        ("Factor", "معامل الحضور", "المعامل الذي نُسِّب به الأساسي (1 = شهر كامل)"),

        // الحركات
        ("IncomeTx", "حركات الدخل", "مجموع حركات الدخل بالفترة"),
        ("OvertimeTx", "العمل الإضافي", "مجموع بنود العمل الإضافي بالفترة"),
        ("SalaryDaysTx", "تعديل أيام الراتب", "صافي الإضافة ناقص الخصم بأيام الراتب"),
        ("LeaveEncashTx", "بدل الإجازة", "مجموع بدل الإجازات المصروف نقداً"),
        ("DeductionTx", "حركات الاقتطاع", "مجموع حركات الاقتطاع بالفترة"),

        // الإجماليات
        ("TotalIncome", "مجموع الدخل", "الإجمالي شاملاً كل الاستحقاقات"),
        ("TotalDeductions", "مجموع الاقتطاعات", "كل ما يُخصم قبل الاستقطاعات النظامية")
    };

    /// <summary>
    /// معطيات الموظف بالفترة — يملؤها المسير من بياناته الفعلية، ويملؤها المختبر
    /// من قيم التجربة. لا مصدر ثالث.
    /// </summary>
    public sealed class Context
    {
        public decimal Basic { get; init; }
        public decimal ProratedBasic { get; init; }
        public decimal Allowances { get; init; }

        public int Days { get; init; }
        public int PresentDays { get; init; }
        public int AbsentDays { get; init; }
        public int UnpaidLeaveDays { get; init; }
        public int DaysInPeriod { get; init; }
        public decimal Hours { get; init; }

        public decimal DailyRate { get; init; }
        public decimal HourlyRate { get; init; }
        public decimal Factor { get; init; } = 1m;

        public decimal IncomeTx { get; init; }
        public decimal OvertimeTx { get; init; }
        public decimal SalaryDaysTx { get; init; }
        public decimal LeaveEncashTx { get; init; }
        public decimal DeductionTx { get; init; }
    }

    /// <summary>
    /// يبني القاموس المُمرَّر للمُقيِّم. غير حسّاس لحالة الأحرف كما يتوقّع المُقيِّم.
    ///
    /// <b>مشتقّان محسوبان هنا لا يُمرَّران:</b> <c>Gross</c> و<c>NonWorkDays</c>
    /// و<c>TotalIncome</c> و<c>TotalDeductions</c> — حتى لا يتسرّب تعريفٌ ثانٍ لها.
    /// </summary>
    /// <summary>
    /// نفس البناء مع دمج القيم الثابتة المسمّاة (<see cref="SalaryConstantStore"/>).
    ///
    /// <b>الأسبقية للمبنيّ دائماً:</b> ثابتٌ اسمه <c>Basic</c> لا يحجب الراتب
    /// الأساسي — يُتجاهَل. (والحفظ يمنعه أصلاً، لكن جدولاً قديماً أو استيراداً قد
    /// يُمرّره، ولا يجوز أن يقلب معنى صيغةٍ قائمة.)
    /// </summary>
    public static Dictionary<string, decimal> Build(Context context, IReadOnlyDictionary<string, decimal>? constants)
    {
        var vars = Build(context);
        if (constants is null) return vars;

        foreach (var (key, value) in constants)
        {
            if (!vars.ContainsKey(key)) vars[key] = value;
        }

        return vars;
    }

    public static Dictionary<string, decimal> Build(Context context)
    {
        var gross = context.Basic + context.Allowances;
        var nonWorkDays = context.Days > 0 ? Math.Max(0, context.DaysInPeriod - context.Days) : 0;
        var totalIncome = context.ProratedBasic + context.Allowances + context.IncomeTx
                          + context.OvertimeTx + context.SalaryDaysTx + context.LeaveEncashTx;

        return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["Basic"] = context.Basic,
            ["ProratedBasic"] = context.ProratedBasic,
            ["Allowances"] = context.Allowances,
            ["Gross"] = gross,

            ["Days"] = context.Days,
            ["PresentDays"] = context.PresentDays,
            ["AbsentDays"] = context.AbsentDays,
            ["UnpaidLeaveDays"] = context.UnpaidLeaveDays,
            ["DaysInPeriod"] = context.DaysInPeriod,
            ["NonWorkDays"] = nonWorkDays,
            ["Hours"] = context.Hours,

            ["DailyRate"] = context.DailyRate,
            ["HourlyRate"] = context.HourlyRate,
            ["Factor"] = context.Factor,

            ["IncomeTx"] = context.IncomeTx,
            ["OvertimeTx"] = context.OvertimeTx,
            ["SalaryDaysTx"] = context.SalaryDaysTx,
            ["LeaveEncashTx"] = context.LeaveEncashTx,
            ["DeductionTx"] = context.DeductionTx,

            ["TotalIncome"] = totalIncome,
            ["TotalDeductions"] = context.DeductionTx
        };
    }
}
