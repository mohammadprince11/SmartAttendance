using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// الدلالة ككيان زمنيّ (الفجوة #7): أيّ الدلالات تُخصم فترتها من مدّة الحضور،
/// وكم منها يُخصم بعد قصّها على نافذتها.
///
/// <para>قبل هذه الميزة كانت رايةُ <c>ShiftTypes.StripSemantics</c> واحدةً للجميع:
/// إمّا تُخصم فترات <b>كل</b> الدلالات غير-الحضورية أو لا تُخصم أيٌّ منها ⟹ لم يكن
/// ممكناً قول «الصلاة 12:00–12:30 لا تُخصم، والدخان يُخصم» — وهو المثال الذي رصدته
/// الدراسة نصّاً.</para>
///
/// <para><b>الضمانة الأولى</b>: الافتراضيات (تُخصم · بلا نافذة) تعطي المجموع السابق
/// نفسه رقماً برقم — فالميزة لا تغيّر مدّة حضورٍ محسوبة اليوم. وهي تمسّ الراتب،
/// فالتغطية إلزامية.</para>
/// </summary>
public class PunchSemanticDeductionTests
{
    private static PunchSemanticStore.PunchSemantic Semantic(
        bool deducted = true, string? from = null, string? to = null) => new()
        {
            Id = 7,
            Name = "استراحة",
            IsDeducted = deducted,
            WindowFrom = from,
            WindowTo = to
        };

    private static DateTime At(string time) =>
        new DateTime(2026, 8, 10).Add(TimeSpan.Parse(time));

    // ═══ ضمانة عدم التغيير ═══

    /// <summary>دلالة بالافتراضيات تُخصم كامل فترتها — سلوك ما قبل الميزة بالحرف.</summary>
    [Fact]
    public void DefaultSemantic_DeductsTheWholeSpan() =>
        Assert.Equal(120, DayAttendanceStore.DeductibleMinutes(
            Semantic(), At("12:00"), At("14:00")));

    // ═══ «الصلاة لا تُخصم والدخان يُخصم» ═══

    [Fact]
    public void NonDeductedSemantic_ContributesNothing() =>
        Assert.Equal(0, DayAttendanceStore.DeductibleMinutes(
            Semantic(deducted: false), At("12:00"), At("12:30")));

    /// <summary>
    /// دلالة مجهولة (حُذفت بعد تسجيل بصمتها) لا تُخصم: لا نخصم من أجر موظفٍ بقاعدةٍ
    /// لا نعرفها. الصمت هنا لصالح الموظف عمداً.
    /// </summary>
    [Fact]
    public void UnknownSemantic_ContributesNothing() =>
        Assert.Equal(0, DayAttendanceStore.DeductibleMinutes(null, At("12:00"), At("14:00")));

    // ═══ النافذة ═══

    /// <summary>استراحةُ ساعتين بنافذة نصف ساعة تُخصم نصف ساعة — والباقي على الموظف.</summary>
    [Fact]
    public void SpanLongerThanWindow_IsClippedToIt() =>
        Assert.Equal(30, DayAttendanceStore.DeductibleMinutes(
            Semantic(from: "12:00", to: "12:30"), At("12:00"), At("14:00")));

    /// <summary>وفترةٌ أقصر من النافذة تُخصم بطولها هي لا بطول النافذة.</summary>
    [Fact]
    public void SpanShorterThanWindow_IsNotInflated() =>
        Assert.Equal(10, DayAttendanceStore.DeductibleMinutes(
            Semantic(from: "12:00", to: "12:30"), At("12:05"), At("12:15")));

    /// <summary>الطرف المتأخر عن بداية النافذة يُقصّ إليها.</summary>
    [Fact]
    public void SpanStartingBeforeWindow_IsClippedAtItsStart() =>
        Assert.Equal(15, DayAttendanceStore.DeductibleMinutes(
            Semantic(from: "12:00", to: "12:30"), At("11:45"), At("12:15")));

    /// <summary>
    /// فترة خارج النافذة كلياً لا تُخصم إطلاقاً: الوقت المصروف خارج نافذته يبقى
    /// محسوباً على الموظف لا مُعفى منه.
    /// </summary>
    [Fact]
    public void SpanEntirelyOutsideWindow_DeductsNothing() =>
        Assert.Equal(0, DayAttendanceStore.DeductibleMinutes(
            Semantic(from: "12:00", to: "12:30"), At("15:00"), At("16:00")));

    /// <summary>حدٌّ واحد فقط مضبوط — يُطبَّق وحده والطرف الآخر حرّ.</summary>
    [Fact]
    public void OneSidedWindow_AppliesOnThatSideOnly()
    {
        Assert.Equal(60, DayAttendanceStore.DeductibleMinutes(
            Semantic(from: "12:00"), At("11:00"), At("13:00")));

        Assert.Equal(60, DayAttendanceStore.DeductibleMinutes(
            Semantic(to: "13:00"), At("12:00"), At("14:00")));
    }

    /// <summary>نافذة معكوسة (من &gt; إلى) لا تُخصم شيئاً بدل أن تعطي رقماً سالباً.</summary>
    [Fact]
    public void InvertedWindow_DeductsNothing_NotANegative() =>
        Assert.Equal(0, DayAttendanceStore.DeductibleMinutes(
            Semantic(from: "13:00", to: "12:00"), At("11:00"), At("14:00")));

    // ═══ حالات حدّية ═══

    /// <summary>
    /// زوج يعبر منتصف الليل لا يُقصّ: النافذة أوقاتُ يومٍ واحد، وقصُّ فترةٍ تمتدّ
    /// ليومين عليها يعطي رقماً بلا معنى. يُخصم كاملاً — سلوك ما قبل النافذة.
    /// </summary>
    [Fact]
    public void SpanCrossingMidnight_IsNotClipped()
    {
        var start = new DateTime(2026, 8, 10, 23, 30, 0);
        var end = new DateTime(2026, 8, 11, 0, 30, 0);

        Assert.Equal(60, DayAttendanceStore.DeductibleMinutes(
            Semantic(from: "12:00", to: "12:30"), start, end));
    }

    /// <summary>زوج ناقص الطرف لا يُخصم — لا مدّة بلا طرفَين.</summary>
    [Fact]
    public void MissingPunchSide_DeductsNothing()
    {
        Assert.Equal(0, DayAttendanceStore.DeductibleMinutes(Semantic(), At("12:00"), null));
        Assert.Equal(0, DayAttendanceStore.DeductibleMinutes(Semantic(), null, At("12:00")));
        Assert.Equal(0, DayAttendanceStore.DeductibleMinutes(Semantic(), null, null));
    }

    /// <summary>خروج قبل دخول (بيانات مقلوبة) لا يعطي دقائق سالبة.</summary>
    [Fact]
    public void ReversedPunches_DeductNothing() =>
        Assert.Equal(0, DayAttendanceStore.DeductibleMinutes(
            Semantic(), At("14:00"), At("12:00")));

    // ═══ التكامل مع الخصم من الساعات ═══

    /// <summary>
    /// الجسر النهائي: الدقائق المخصومة تُطرح من ساعات العمل. نصف ساعة من ثماني
    /// ساعات = 7.5 — لا 7.48 ولا 8.
    /// </summary>
    [Fact]
    public void DeductedMinutes_LowerTheWorkedHours()
    {
        var minutes = DayAttendanceStore.DeductibleMinutes(
            Semantic(from: "12:00", to: "12:30"), At("12:00"), At("14:00"));

        Assert.Equal(7.5m, DayAttendanceStore.StripSemanticHours(8m, minutes));
    }

    /// <summary>ودلالةٌ لا تُخصم تُبقي الساعات كما هي.</summary>
    [Fact]
    public void NonDeductedSemantic_LeavesWorkedHoursUntouched()
    {
        var minutes = DayAttendanceStore.DeductibleMinutes(
            Semantic(deducted: false), At("12:00"), At("14:00"));

        Assert.Equal(8m, DayAttendanceStore.StripSemanticHours(8m, minutes));
    }
}
