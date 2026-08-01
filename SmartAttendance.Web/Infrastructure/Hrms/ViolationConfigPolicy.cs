namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// تهيئة المخالفات (نظير `/Employees/ViolationConfiguration` بكيان) — منطق نقيّ.
///
/// ثلاث فجوات كان غيابها يجعل الانضباط عندنا **إجراءً بلا ضمانات**:
/// <list type="number">
/// <item><b>مهلة الاعتراض</b> — للموظف حقّ الطعن خلال مدّة، وبعدها تُصبح العقوبة
/// نهائية. بلا تاريخ محسوب يبقى «هل فات الوقت؟» رأياً لا واقعة.</item>
/// <item><b>تاريخ إسقاط العقوبة</b> — العقوبات لا تبقى بسجلّ الموظف للأبد؛ تسقط
/// بعد مدّة فلا تُحتسب بتدرّج العقوبة اللاحق. غيابه يعني موظفاً يُعاقَب اليوم
/// كمكرِّر بسبب مخالفةٍ عمرها ثلاث سنوات.</item>
/// <item><b>رقم الفقرة من لائحة الجزاءات</b> — سند العقوبة النظامي.</item>
/// </list>
///
/// كل التواريخ تُحسب من **تاريخ التبليغ** لا من تاريخ وقوع المخالفة: المهلة تبدأ
/// حين يَعلم الموظف، وهذا فرق يحسم الطعون.
/// </summary>
public static class ViolationConfigPolicy
{
    public const string KeyObjectionDays = "Violation.ObjectionDays";
    public const string KeyRequireReply = "Violation.RequireEmployeeReply";
    public const string KeyReplyDays = "Violation.ReplyDays";
    public const string KeyRequireCommittee = "Violation.RequireCommitteeApproval";
    public const string KeyDropMonths = "Violation.DropMonths";
    public const string KeyRefPrefix = "Violation.RefPrefix";

    public const int DefaultObjectionDays = 7;
    public const int DefaultReplyDays = 3;
    public const int DefaultDropMonths = 12;

    /// <summary>
    /// آخر يوم يحقّ فيه الاعتراض. مهلة غير موجبة ⟹ <c>null</c> = **لا اعتراض**،
    /// وهو معنى مختلف عن «انتهت المهلة»؛ الخلط بينهما يُسقِط حقّاً بلا قرار.
    /// </summary>
    public static DateOnly? ObjectionDeadline(DateOnly? notifiedOn, int objectionDays)
    {
        if (notifiedOn is not { } notified || objectionDays <= 0)
        {
            return null;
        }

        return notified.AddDays(objectionDays);
    }

    /// <summary>هل باب الاعتراض ما زال مفتوحاً؟ بلا مهلة معرَّفة ⟹ مغلق.</summary>
    public static bool CanObject(DateOnly? notifiedOn, int objectionDays, DateOnly asOf) =>
        ObjectionDeadline(notifiedOn, objectionDays) is { } deadline && asOf <= deadline;

    /// <summary>
    /// تاريخ سقوط العقوبة من السجلّ. مدّة غير موجبة ⟹ <c>null</c> = **لا تسقط أبداً**
    /// (سلوك النظام اليوم حرفياً)، فتبقى البيانات القائمة على معناها ما لم يُهيَّأ.
    /// </summary>
    public static DateOnly? DropDate(DateOnly? decidedOn, int dropMonths)
    {
        if (decidedOn is not { } decided || dropMonths <= 0)
        {
            return null;
        }

        return decided.AddMonths(dropMonths);
    }

    /// <summary>
    /// هل سقطت العقوبة؟ الساقطة **لا تُحتسب بتدرّج العقوبة** — وهذا هو أثرها العملي
    /// الوحيد؛ لا تُحذف ولا تُخفى، بل تخرج من العدّ.
    /// </summary>
    public static bool IsDropped(DateOnly? decidedOn, int dropMonths, DateOnly asOf) =>
        DropDate(decidedOn, dropMonths) is { } drop && asOf >= drop;

    /// <summary>
    /// عدد المخالفات **الفعّالة** لتدرّج العقوبة: ما لم يسقط بعد.
    /// هذه الدالة هي سبب وجود «تاريخ الإسقاط» أصلاً.
    /// </summary>
    public static int CountableViolations(
        IEnumerable<DateOnly?> decisionDates,
        int dropMonths,
        DateOnly asOf) =>
        decisionDates.Count(date => date is not null && !IsDropped(date, dropMonths, asOf));

    /// <summary>
    /// الرقم المرجعي السنوي (نمط كيان <c>VC26-1</c>): بادئة + آخر رقمين من السنة +
    /// تسلسل. التسلسل يُعاد من واحد كل سنة، فالرقم يبقى قصيراً ومقروءاً.
    /// </summary>
    public static string ReferenceNumber(string? prefix, int year, int sequence)
    {
        var head = string.IsNullOrWhiteSpace(prefix) ? "VC" : prefix.Trim();
        return $"{head}{year % 100:00}-{sequence}";
    }

    /// <summary>حالة الحالة كما تُعرض: تراعي السقوط والاعتراض معاً.</summary>
    public static string StatusLabel(
        DateOnly? notifiedOn,
        DateOnly? decidedOn,
        int objectionDays,
        int dropMonths,
        DateOnly asOf)
    {
        if (IsDropped(decidedOn, dropMonths, asOf))
        {
            return "أُسقِطت";
        }

        if (CanObject(notifiedOn, objectionDays, asOf))
        {
            var deadline = ObjectionDeadline(notifiedOn, objectionDays)!.Value;
            var left = deadline.DayNumber - asOf.DayNumber;
            return $"باب الاعتراض مفتوح ({left} يوم)";
        }

        return decidedOn is null ? "قيد النظر" : "نهائية";
    }
}
