using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات سياسة ربط الراتب بالحضور.
///
/// الأول والأهم اختبار **انحدار**: السياسة الافتراضية (متساهل · خصم يوم بيوم ·
/// بلا سالب) تعيد معامل ما قبل هذه السياسة حرفياً — <c>(أيام العمل − الغياب) ÷
/// أيام العمل</c>، و1 عند غياب البيانات. أي انحراف هنا يغيّر كل قسيمة بأثر رجعي.
///
/// والبقية تحرس ما طلبه محمد صراحةً: الراتب على قدر الأيام المداوَمة، وخصم
/// الغياب بمعامل السياسة (يوم بيومين…) حتى لو أخرج الصافي سالباً.
/// </summary>
public class AttendanceSalaryLinkTests
{
    private static AttendanceSalaryLink.Decision Eval(
        string mode, int workDays, int presentDays, int absentDays,
        decimal workedHours = 0m, decimal absenceDays = 1m, bool allowNegative = false) =>
        AttendanceSalaryLink.Evaluate(
            new AttendanceSalaryLink.Policy(mode, absenceDays, allowNegative),
            workDays, presentDays, absentDays, workedHours);

    [Fact]
    public void DefaultPolicy_WithData_MatchesPrePolicyFormula()
    {
        var d = Eval(AttendanceSalaryLink.Lenient, workDays: 26, presentDays: 24, absentDays: 2);

        Assert.True(d.Include);
        Assert.Equal(24m / 26m, d.Factor);
        Assert.Null(d.Note);   // حضور طبيعي لا يحتاج إعلاناً
    }

    [Fact]
    public void Lenient_WithoutData_PaysInFull_ButDeclaresIt()
    {
        var d = Eval(AttendanceSalaryLink.Lenient, 0, 0, 0);

        Assert.True(d.Include);
        Assert.Equal(1m, d.Factor);          // نفس سلوك ما قبل السياسة
        Assert.NotNull(d.Note);              // لكنه لم يعد صامتاً
        Assert.Contains("كاملاً", d.Note);
    }

    [Fact]
    public void Strict_WithoutData_DoesNotCalculateAtAll()
    {
        var d = Eval(AttendanceSalaryLink.Strict, 0, 0, 0);

        Assert.False(d.Include);
        Assert.Equal(0m, d.Factor);
        Assert.NotNull(d.Note);
    }

    [Fact]
    public void PresentDays_PaysExactlyForDaysAttended()
    {
        // «مداوم 10 أيام ⟹ راتب 10 أيام» — طلب محمد حرفياً
        var d = Eval(AttendanceSalaryLink.PresentDays, workDays: 26, presentDays: 10, absentDays: 16);

        Assert.True(d.Include);
        Assert.Equal(10m / 26m, d.Factor);
        Assert.Contains("10", d.Note);
    }

    [Fact]
    public void PresentDays_ZeroAttendance_PaysNothing()
    {
        var d = Eval(AttendanceSalaryLink.PresentDays, 26, 0, 26);

        Assert.True(d.Include);
        Assert.Equal(0m, d.Factor);
    }

    [Fact]
    public void PresentDays_DiffersFromLenient_WhenDaysAreNeitherPresentNorAbsent()
    {
        // 26 يوم عمل · 20 حاضراً · 2 غياباً · 4 أيام أخرى (ناقصة/إجازة)
        var present = Eval(AttendanceSalaryLink.PresentDays, 26, 20, 2);
        var lenient = Eval(AttendanceSalaryLink.Lenient, 26, 20, 2);

        Assert.Equal(20m / 26m, present.Factor);
        Assert.Equal(24m / 26m, lenient.Factor);   // الأيام غير المسجّلة غياباً تُدفع
    }

    [Fact]
    public void AbsenceMultiplier_DeductsExtraDaysOnTop()
    {
        // 26 يوم عمل · غياب يومين · المعامل 2 ⟹ يُخصم 4 أيام لا يومان
        var d = Eval(AttendanceSalaryLink.Lenient, 26, 24, 2, absenceDays: 2m);

        Assert.Equal((26m - 4m) / 26m, d.Factor);
        Assert.Contains("مضاعف", d.Note);
    }

    [Fact]
    public void AbsenceMultiplier_CanDriveSalaryNegative_WhenExplicitlyAllowed()
    {
        // 20 يوم عمل · 15 غياباً · المعامل 2 ⟹ خصم 30 يوماً من 20 ⟹ سالب
        var d = Eval(AttendanceSalaryLink.Lenient, 20, 5, 15, absenceDays: 2m, allowNegative: true);

        Assert.True(d.Factor < 0m);
        Assert.Equal((20m - 30m) / 20m, d.Factor);
    }

    [Fact]
    public void WithoutAllowNegative_TheFactorStopsAtZero_NoDebt()
    {
        var d = Eval(AttendanceSalaryLink.Lenient, 20, 5, 15, absenceDays: 2m, allowNegative: false);

        Assert.Equal(0m, d.Factor);
    }

    [Fact]
    public void AbsenceMultiplierOfOne_ChangesNothing()
    {
        var withFactor = Eval(AttendanceSalaryLink.Lenient, 26, 24, 2, absenceDays: 1m);
        var plain = Eval(AttendanceSalaryLink.Lenient, 26, 24, 2);

        Assert.Equal(plain.Factor, withFactor.Factor);
        Assert.Null(withFactor.Note);
    }

    [Fact]
    public void Hours_ProratesByActualHours()
    {
        // 20 يوم عمل × 8 = 160 ساعة متوقّعة، عُملت 120 ⟹ 75%
        var d = Eval(AttendanceSalaryLink.Hours, 20, 20, 0, workedHours: 120m);

        Assert.True(d.Include);
        Assert.Equal(0.75m, d.Factor);
        Assert.Contains("120", d.Note);
    }

    [Fact]
    public void Hours_NeverExceedsFullSalary_OvertimeIsASeparateItem()
    {
        var d = Eval(AttendanceSalaryLink.Hours, 20, 20, 0, workedHours: 400m);

        Assert.Equal(1m, d.Factor);
    }

    [Fact]
    public void Hours_WithoutData_DoesNotCalculate_NoDivisionByZero()
    {
        var d = Eval(AttendanceSalaryLink.Hours, 0, 0, 0, workedHours: 90m);

        Assert.False(d.Include);
    }

    [Fact]
    public void AbsentDaysBeyondWorkDays_ClampToZero_UnlessNegativeAllowed()
    {
        Assert.Equal(0m, Eval(AttendanceSalaryLink.Lenient, 20, 0, 25).Factor);
        Assert.Equal(-0.25m, Eval(AttendanceSalaryLink.Lenient, 20, 0, 25, allowNegative: true).Factor);
    }

    [Fact]
    public void UnknownMode_FallsBackToLenient_TheNonBreakingDefault()
    {
        Assert.Equal(AttendanceSalaryLink.Lenient, AttendanceSalaryLink.NormalizeMode(null));
        Assert.Equal(AttendanceSalaryLink.Lenient, AttendanceSalaryLink.NormalizeMode("Nonsense"));
        Assert.Equal(AttendanceSalaryLink.Strict, AttendanceSalaryLink.NormalizeMode("Strict"));
        Assert.Equal(AttendanceSalaryLink.PresentDays, AttendanceSalaryLink.NormalizeMode("PresentDays"));
        Assert.Equal(AttendanceSalaryLink.Hours, AttendanceSalaryLink.NormalizeMode("Hours"));
    }

    [Fact]
    public void NegativeAbsenceFactor_IsRejected_NotTurnedIntoABonus()
    {
        var d = Eval(AttendanceSalaryLink.Lenient, 26, 24, 2, absenceDays: -3m);

        Assert.Equal((26m - 2m) / 26m, d.Factor);   // يعود ليوم-بيوم
    }

    [Fact]
    public void MonthRowWithZeroWorkDays_CountsAsNoData_NotAsZeroAttendance()
    {
        // صفّ اعتماد موجود بأيام عمل صفر (بلا مناوبة مسنَدة) — القسمة مستحيلة،
        // وادّعاء «صفر حضور» يصفّر راتباً بلا سند.
        Assert.False(AttendanceSalaryLink.HasAttendanceData(0));
        Assert.True(AttendanceSalaryLink.HasAttendanceData(1));
    }

    // ── مقام التنسيب الشهري (Fixed30 / أيام الفترة) — أيام الراحة/العطل مدفوعة ──

    private static AttendanceSalaryLink.Decision EvalDiv(
        string mode, int workDays, int presentDays, int absentDays, int divisor,
        decimal workedHours = 0m, decimal absenceDays = 1m) =>
        AttendanceSalaryLink.Evaluate(
            new AttendanceSalaryLink.Policy(mode, absenceDays, false, 8m, divisor),
            workDays, presentDays, absentDays, workedHours);

    [Fact]
    public void Fixed30_DeductsAbsenceOverThirty_NotOverWorkDays()
    {
        // بلاغ محمد: أساسي 400,000 نزل 369,230.77 لأن المقام كان 26. الشهري: غياب
        // يومٍ يُخصم ÷30 لا ÷26، وأيام الراحة (30−26) مدفوعة.
        var d = EvalDiv(AttendanceSalaryLink.Lenient, workDays: 26, presentDays: 25, absentDays: 1, divisor: 30);
        Assert.Equal(29m / 30m, d.Factor);            // 400000×29/30 = 386,666.67 لا 369,230.77
    }

    [Fact]
    public void Fixed30_ZeroAbsence_PaysFull_IncludingRestDays()
    {
        var d = EvalDiv(AttendanceSalaryLink.Lenient, workDays: 26, presentDays: 26, absentDays: 0, divisor: 30);
        Assert.Equal(1m, d.Factor);                   // بلا غياب ⟹ الأساسي كاملاً رغم أيام الراحة
    }

    [Fact]
    public void Divisor0_IsExactlyOldWorkDaysBehaviour()
    {
        // المقام الافتراضي (0) يجب أن يطابق المعادلة القديمة حرفياً لكل نمط.
        Assert.Equal(24m / 26m,
            EvalDiv(AttendanceSalaryLink.Lenient, 26, 24, 2, divisor: 0).Factor);
        Assert.Equal(20m / 26m,
            EvalDiv(AttendanceSalaryLink.PresentDays, 26, 20, 0, divisor: 0).Factor);
        Assert.Equal(192m / 208m,
            EvalDiv(AttendanceSalaryLink.Hours, 26, 0, 0, divisor: 0, workedHours: 192m).Factor);
    }

    [Fact]
    public void Fixed30_PresentDaysMode_UnworkedWorkDaysOverThirty()
    {
        // نمط أيام الحضور بمقام 30: أيام الدوام غير المحضورة (26−24=2) تُخصم ÷30.
        var d = EvalDiv(AttendanceSalaryLink.PresentDays, workDays: 26, presentDays: 24, absentDays: 0, divisor: 30);
        Assert.Equal(28m / 30m, d.Factor);
    }

    // ── تنسيب المُعيَّن منتصف الشهر (أيام قبل التعيين غير مدفوعة) ──

    [Fact]
    public void MidMonthHire_Day5_Pays26Of30_WhenNoAbsence()
    {
        // تعيّن يوم 5 ⟹ 4 أيام قبل المباشرة (1..4) غير مدفوعة ⟹ 26/30 (اصطلاح «26 شاملاً»).
        var d = AttendanceSalaryLink.Evaluate(
            new AttendanceSalaryLink.Policy(AttendanceSalaryLink.Lenient, 1m, false, 8m, 30),
            workDays: 22, presentDays: 22, absentDays: 0, workedHours: 0m, preEmploymentUnpaidDays: 4);
        Assert.Equal(26m / 30m, d.Factor);           // 400000×26/30 = 346,666.67
        Assert.Contains("تعيين", d.Note);
    }

    [Fact]
    public void MidMonthHire_CombinesWithAbsence()
    {
        // 4 أيام قبل التعيين + غياب يوم ⟹ (4+1) غير مدفوع ⟹ 25/30.
        var d = AttendanceSalaryLink.Evaluate(
            new AttendanceSalaryLink.Policy(AttendanceSalaryLink.Lenient, 1m, false, 8m, 30),
            workDays: 22, presentDays: 21, absentDays: 1, workedHours: 0m, preEmploymentUnpaidDays: 4);
        Assert.Equal(25m / 30m, d.Factor);
    }

    [Fact]
    public void NoPreEmploymentDays_IsUnchanged()
    {
        // موظف كامل الشهر (0 أيام قبل التعيين) ⟹ لا أثر — السلوك القديم.
        var d = AttendanceSalaryLink.Evaluate(
            new AttendanceSalaryLink.Policy(AttendanceSalaryLink.Lenient, 1m, false, 8m, 30),
            workDays: 26, presentDays: 25, absentDays: 1, workedHours: 0m, preEmploymentUnpaidDays: 0);
        Assert.Equal(29m / 30m, d.Factor);
        Assert.Null(d.Note);
    }
}
