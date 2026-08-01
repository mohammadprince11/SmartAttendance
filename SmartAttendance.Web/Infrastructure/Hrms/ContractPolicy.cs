namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// منطق العقود النقيّ — «الأيام المتبقية» والتمييز الجوهري بين **التمديد** و**التجديد**.
///
/// الفجوة التي يسدّها هذا الملف بنيوية لا شكلية: عندنا كان العقد **حالةً راهنة**
/// (ثلاثة حقول بالملف تُدهَس عند التغيير)، وعند كيان **كيانٌ له تاريخ**. وأثر ذلك
/// أن سؤال «كم عقداً جُدِّد لهذا الموظف؟» كان بلا جواب، و**مكافأة نهاية الخدمة قد
/// تُحتسب خطأً** لمن تعاقب على عقود لأننا لا نميّز الخدمة المتصلة من المنقطعة.
///
/// ⭐ الفرق الذي يقرّر المال:
/// <list type="bullet">
/// <item><b>تمديد</b>: نفس العقد يمتدّ تاريخه ⟹ رقم عقد واحد و**خدمة متصلة**.</item>
/// <item><b>تجديد</b>: عقد **جديد** يبدأ بعد انتهاء السابق ⟹ رقم جديد، وقد تُعاد
/// فترة تجربة، والاتصال قرارٌ صريح لا افتراض.</item>
/// </list>
/// </summary>
public static class ContractPolicy
{
    public const string MovementExtend = "Extend";
    public const string MovementRenew = "Renew";
    public const string MovementAmend = "Amend";

    public static string MovementLabel(string? kind) => kind switch
    {
        MovementExtend => "تمديد العقد الحالي",
        MovementRenew => "تجديد عقد",
        MovementAmend => "تعديل عقد",
        _ => "حركة"
    };

    /// <summary>
    /// الأيام المتبقية على انتهاء العقد. <c>null</c> = عقد **غير محدود** — ويُعرض
    /// نصّاً لا رقماً كما بكيان، لأن صفراً هنا يعني «منتهٍ» وهو عكس المقصود تماماً.
    /// والمنتهي يُرجع صفراً لا سالباً: العدّاد لا ينزل تحت الصفر بالعرض.
    /// </summary>
    public static int? RemainingDays(DateOnly? toDate, DateOnly asOf)
    {
        if (toDate is not { } end)
        {
            return null;
        }

        var days = end.DayNumber - asOf.DayNumber;
        return days < 0 ? 0 : days;
    }

    public static string RemainingLabel(DateOnly? toDate, DateOnly asOf)
    {
        var days = RemainingDays(toDate, asOf);
        return days is null ? "غير محدود" : $"{days} يوم";
    }

    /// <summary>عقد سارٍ الآن: بدأ ولم ينتهِ. غير المحدود سارٍ ما دام قد بدأ.</summary>
    public static bool IsCurrent(DateOnly fromDate, DateOnly? toDate, DateOnly asOf) =>
        fromDate <= asOf && (toDate is null || toDate >= asOf);

    /// <summary>
    /// تاريخ انتهاء مشتقّ من **المدة الافتراضية لنوع العقد** (نمط كيان: «يعتمد
    /// تاريخ انتهاء العقد على مدة العقد الافتراضية»). مدة غير موجبة ⟹ عقد غير
    /// محدود، وهو التمثيل الصحيح لـ«بلا مدة» بدل تاريخٍ مخترَع.
    ///
    /// يُطرح يومٌ لأن عقد سنةٍ يبدأ 2022/01/01 ينتهي 2022/12/31 لا 2023/01/01 —
    /// خطأ اليوم الواحد هنا يزحزح كل تنبيهات الانتهاء ومدة الخدمة.
    /// </summary>
    public static DateOnly? DeriveEndDate(DateOnly fromDate, int? defaultMonths)
    {
        if (defaultMonths is not { } months || months <= 0)
        {
            return null;
        }

        return fromDate.AddMonths(months).AddDays(-1);
    }

    /// <summary>
    /// بداية العقد التالي بعد <paramref name="previousEnd"/>.
    /// **تمديد** يبقي البداية كما هي (نفس العقد)، و**تجديد** يبدأ اليوم التالي
    /// لانتهاء السابق فلا يتداخل عقدان بيومٍ واحد.
    /// </summary>
    public static DateOnly NextStartDate(string movementKind, DateOnly currentStart, DateOnly? previousEnd) =>
        movementKind == MovementExtend || previousEnd is null
            ? currentStart
            : previousEnd.Value.AddDays(1);

    /// <summary>
    /// أشهر الخدمة **المتصلة** عبر سلسلة عقود مرتّبة زمنياً.
    ///
    /// القاعدة: فجوةٌ بين عقدٍ والذي يليه تقطع الاتصال، فتُحتسب الخدمة من العقد
    /// اللاحق فقط. تحمُّل يومٍ واحد مقصود (انتهاء 12/31 وبداية 01/01 اتصالٌ لا قطع).
    ///
    /// ⚠️ هذه الدالة **لا تُستعمل بحساب نهاية الخدمة القائم** — تُعرض بالسجلّ فقط
    /// حتى تُراجَع مالياً بطلب صريح. تغيير صيغة نهاية الخدمة ممنوع بلا طلبك.
    /// </summary>
    public static int ContinuousServiceMonths(
        IReadOnlyList<(DateOnly From, DateOnly? To)> contracts,
        DateOnly asOf)
    {
        if (contracts.Count == 0)
        {
            return 0;
        }

        var ordered = contracts.OrderBy(contract => contract.From).ToList();
        var start = ordered[^1].From;

        for (var index = ordered.Count - 1; index > 0; index--)
        {
            var previousEnd = ordered[index - 1].To;
            if (previousEnd is null)
            {
                start = ordered[index - 1].From;
                continue;
            }

            var gap = ordered[index].From.DayNumber - previousEnd.Value.DayNumber;
            if (gap > 1)
            {
                break;
            }

            start = ordered[index - 1].From;
        }

        if (start > asOf)
        {
            return 0;
        }

        var months = ((asOf.Year - start.Year) * 12) + asOf.Month - start.Month;
        if (asOf.Day < start.Day)
        {
            months--;
        }

        return Math.Max(months, 0);
    }
}
