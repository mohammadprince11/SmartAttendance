namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// تركيب «أوعية» الاحتساب (الضريبة/الضمان) من مكوّنات القسيمة.
///
/// قبل هذا الملف كان الوعاءان مثبّتين داخل <see cref="PayrollRunStore"/>:
/// وعاء الضريبة = الأساسي + العلاوات + الخاضع للضريبة، ووعاء الضمان = الإجمالي
/// كاملاً بلا أي تحكّم. فُصلا هنا كدالة نقية لسببين: أنهما صارا قابلين للاختبار
/// بلا قاعدة بيانات، وأن **العضوية صارت بيانات** — قائمة مفاتيح — تمهيداً لأن
/// يحرّرها المستخدم بالسحب والإفلات بدل أن تكون ثابتة بالكود.
///
/// ⚠️ الافتراضيان أدناه يجب أن ينتجا **نفس أرقام المسير الحالي بالضبط**؛
/// أي انحراف هنا يظهر باقتطاع كل قسيمة. الاختبارات تثبّت هذا التكافؤ.
/// </summary>
public static class SalaryBaseComposer
{
    /// <summary>مفاتيح المكوّنات المتاحة للانضمام لأي وعاء (مفتاح ← تسمية عربية).</summary>
    public static readonly (string Key, string Label)[] Components =
    {
        ("Basic", "الراتب الأساسي (منسَّب بالحضور)"),
        ("Allowances", "العلاوات (الكل)"),
        ("TaxableAllowances", "العلاوات الخاضعة للضريبة فقط"),
        ("GosiAllowances", "العلاوات المؤهَّلة للضمان فقط"),
        ("TaxableIncome", "بنود الدخل الخاضعة"),
        ("TaxableOvertime", "العمل الإضافي الخاضع"),
        ("SalaryDays", "تسوية أيام الراتب"),
        ("LeaveEncashment", "بدل الإجازات الخاضع"),
        ("FormulaAdd", "إضافات المعادلات الخاضعة"),
        ("Gross", "الراتب الإجمالي")
    };

    /// <summary>مفتاح الوعاء النظامي للضريبة.</summary>
    public const string TaxBaseKey = "TaxBase";

    /// <summary>مفتاح الوعاء النظامي للضمان.</summary>
    public const string GosiBaseKey = "GosiBase";

    /// <summary>عضوية وعاء الضريبة كما كانت مثبّتة بالمحرك قبل الفصل.</summary>
    public static readonly string[] DefaultTaxMembers =
    {
        "Basic", "Allowances", "TaxableIncome", "TaxableOvertime",
        "SalaryDays", "LeaveEncashment", "FormulaAdd"
    };

    /// <summary>عضوية وعاء الضمان كما كانت مثبّتة: الإجمالي كاملاً.</summary>
    public static readonly string[] DefaultGosiMembers = { "Gross" };

    /// <summary>مبالغ مكوّنات قسيمة موظف واحد لشهر واحد.</summary>
    public sealed class Amounts
    {
        public decimal Basic { get; set; }
        public decimal Allowances { get; set; }
        public decimal TaxableAllowances { get; set; }
        public decimal GosiAllowances { get; set; }
        public decimal TaxableIncome { get; set; }
        public decimal TaxableOvertime { get; set; }
        public decimal SalaryDays { get; set; }
        public decimal LeaveEncashment { get; set; }
        public decimal FormulaAdd { get; set; }
        public decimal Gross { get; set; }

        public decimal Of(string key) => key switch
        {
            "Basic" => Basic,
            "Allowances" => Allowances,
            "TaxableAllowances" => TaxableAllowances,
            "GosiAllowances" => GosiAllowances,
            "TaxableIncome" => TaxableIncome,
            "TaxableOvertime" => TaxableOvertime,
            "SalaryDays" => SalaryDays,
            "LeaveEncashment" => LeaveEncashment,
            "FormulaAdd" => FormulaAdd,
            "Gross" => Gross,
            _ => 0m
        };
    }

    /// <summary>
    /// يجمع مبالغ المكوّنات المنضمّة للوعاء. مفتاح غير معروف يُتجاهَل بصمت عمداً:
    /// عضوية محفوظة بالقاعدة قد تشير لمكوّن أُزيل، والأولى ألا يسقط المسير كلّه.
    /// </summary>
    public static decimal Compose(Amounts amounts, IEnumerable<string>? members)
    {
        if (amounts == null || members == null) return 0m;

        decimal total = 0m;
        foreach (var key in members)
        {
            if (!string.IsNullOrWhiteSpace(key)) total += amounts.Of(key.Trim());
        }

        return decimal.Round(total, 2);
    }

    /// <summary>وعاء الضريبة بالعضوية الافتراضية (سلوك ما قبل الفصل).</summary>
    public static decimal TaxBase(Amounts amounts) => Compose(amounts, DefaultTaxMembers);

    /// <summary>وعاء الضمان بالعضوية الافتراضية (سلوك ما قبل الفصل).</summary>
    public static decimal GosiBase(Amounts amounts) => Compose(amounts, DefaultGosiMembers);
}
