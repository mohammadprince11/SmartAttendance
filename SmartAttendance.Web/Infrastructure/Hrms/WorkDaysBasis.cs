namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// مقام «معدّل اليوم» — **مُعلَن لا ضمنيّ**.
///
/// كان المسير يحسب اليومي بـ<c>Basic ÷ 30</c> مثبّتاً بالكود. و«30» ليست حقيقة
/// بل **قراراً**: شهرٌ بـ31 يوماً، وسنةٌ ماليّة معدّلها 30.4167، وشركةٌ تستثني
/// عطلها من المقام — كلٌّ يعطي رقماً مختلفاً. وخصمُ يومٍ بمقامٍ لا يعرفه الموظف
/// **رقمٌ لا يُدافَع عنه**؛ لذلك صار المقام حقلاً يُختار ويُشرح بالكتاب الرسمي.
///
/// كيان تعرضه ثلاثة خيارات صريحة، وهي المنقولة هنا.
/// </summary>
public static class WorkDaysBasis
{
    /// <summary>معدّل السنة المالية: 365 ÷ 12 = 30.4167 يوماً بالشهر.</summary>
    public const string FiscalYear = "FiscalYear";

    /// <summary>أيام فترة الراتب الفعلية (28 · 30 · 31).</summary>
    public const string Period = "Period";

    /// <summary>عدد ثابت تحدّده الشركة — الافتراضي 30 (سلوك ما قبل هذه الميزة).</summary>
    public const string Fixed = "Fixed";

    public static readonly IReadOnlyList<(string Key, string Label)> Options = new[]
    {
        (Fixed,      "عدد ثابت"),
        (Period,     "أيام فترة الراتب"),
        (FiscalYear, "معدّل السنة المالية")
    };

    /// <summary>الافتراضي 30 يوماً — يبقي كل قسيمة قائمة على رقمها الحالي.</summary>
    public const int DefaultFixedDays = 30;

    public static string Label(string? key) =>
        Options.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase)).Label ?? "عدد ثابت";

    public static string Normalize(string? key) =>
        Options.Any(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase))
            ? Options.First(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase)).Key
            : Fixed;

    // ── استثناء العطل من المقام ──────────────────────────────────────────────

    public const string ExcludeNone = "None";
    public const string ExcludeCompanyHolidays = "CompanyHolidays";
    public const string ExcludeRestDays = "RestDays";
    public const string ExcludeBoth = "Both";

    /// <summary>
    /// ⚠️ **الخياران المعروضان اثنان لا أربعة.** كيان تفصل «عطل الشركة» عن «أيام
    /// الراحة»، ومصدرنا الوحيد اليوم هو <c>MonthAttendance.WorkDays</c> — وهو
    /// يطرحهما معاً بلا تمييز. عرض أربعة خيارات ثلاثةٌ منها تعطي نفس الرقم هو
    /// بالضبط «الحقل الأصمّ» الذي عالجناه مراراً. المفتاحان الآخران يبقيان
    /// بالنموذج لقراءة صفوفٍ قديمة، ويُعرضان حين يصير للعطل مصدرٌ مستقلّ.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Label)> ExcludeOptions = new[]
    {
        (ExcludeNone,     "لا شيء — المقام كامل"),
        (ExcludeRestDays, "أيام غير العمل (العطل والراحة)")
    };

    /// <summary>كل المفاتيح المفهومة — للتحقّق والقراءة لا للعرض.</summary>
    private static readonly string[] AllExcludeKeys =
    {
        ExcludeNone, ExcludeCompanyHolidays, ExcludeRestDays, ExcludeBoth
    };

    public static string ExcludeLabel(string? key) => NormalizeExclude(key) switch
    {
        ExcludeCompanyHolidays => "أيام عطل الشركة",
        ExcludeRestDays => "أيام غير العمل (العطل والراحة)",
        ExcludeBoth => "العطل وأيام الراحة",
        _ => "لا شيء"
    };

    public static string NormalizeExclude(string? key) =>
        AllExcludeKeys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
        ?? ExcludeNone;

    /// <summary>كم يوماً يُطرح من المقام بحسب قرار الاستثناء.</summary>
    public static int ExcludedDays(string? excludeMode, int companyHolidays, int restDays) =>
        NormalizeExclude(excludeMode) switch
        {
            ExcludeCompanyHolidays => Math.Max(0, companyHolidays),
            ExcludeRestDays => Math.Max(0, restDays),
            ExcludeBoth => Math.Max(0, companyHolidays) + Math.Max(0, restDays),
            _ => 0
        };

    // ── المقام ───────────────────────────────────────────────────────────────

    /// <summary>
    /// المقام النهائي بعد الاستثناء.
    ///
    /// ⚠️ **لا يقلّ عن يوم واحد أبداً.** شركةٌ تستثني كل أيام الشهر تصنع قسمةً على
    /// صفر — أي راتباً يومياً لانهائياً وخصماً يبتلع القسيمة كلّها. الحدّ الأدنى
    /// يفشل بأمانٍ نحو رقمٍ كبير لا نحو كارثة.
    /// </summary>
    public static decimal Divisor(
        string? basis,
        int fixedDays,
        int daysInPeriod,
        int companyHolidays = 0,
        int restDays = 0,
        string? excludeMode = null)
    {
        var raw = Normalize(basis) switch
        {
            FiscalYear => 365m / 12m,
            Period => daysInPeriod > 0 ? daysInPeriod : DefaultFixedDays,
            _ => fixedDays > 0 ? fixedDays : DefaultFixedDays
        };

        // معدّل السنة المالية رقمٌ نظاميّ لا فترة تقويمية، فلا يُطرح منه عطل.
        if (!string.Equals(Normalize(basis), FiscalYear, StringComparison.Ordinal))
        {
            raw -= ExcludedDays(excludeMode, companyHolidays, restDays);
        }

        return raw < 1m ? 1m : decimal.Round(raw, 4);
    }

    /// <summary>معدّل اليوم = وعاء الخصم ÷ المقام.</summary>
    public static decimal DailyRate(decimal pool, decimal divisor) =>
        divisor <= 0 ? 0m : decimal.Round(pool / divisor, 4);

    /// <summary>معدّل الساعة = معدّل اليوم ÷ ساعات اليوم (افتراضها ثمانٍ).</summary>
    public static decimal HourlyRate(decimal dailyRate, decimal hoursPerDay = 8m) =>
        hoursPerDay <= 0 ? 0m : decimal.Round(dailyRate / hoursPerDay, 4);

    /// <summary>شرحٌ عربي للمقام — يُطبع بالكتاب كي يعرف الموظف على أيّ أساس خُصم.</summary>
    public static string Describe(string? basis, int fixedDays, string? excludeMode)
    {
        var head = Normalize(basis) switch
        {
            FiscalYear => "معدّل السنة المالية (30.4167 يوماً)",
            Period => "أيام فترة الراتب الفعلية",
            _ => $"{(fixedDays > 0 ? fixedDays : DefaultFixedDays)} يوماً"
        };

        var exclude = NormalizeExclude(excludeMode);
        return exclude == ExcludeNone ? head : $"{head} بعد استثناء {ExcludeLabel(exclude)}";
    }
}
