using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات سياسة ربط الراتب بالحضور.
///
/// الأول والأهم اختبار **انحدار**: النمط الافتراضي (المتساهل) يجب أن يعيد معامل
/// ما قبل هذه السياسة حرفياً — <c>(أيام العمل − الغياب) ÷ أيام العمل</c>، و1
/// عند غياب البيانات. أي انحراف هنا يغيّر كل قسيمة بأثر رجعي.
///
/// والبقية تحرس ما كان صامتاً: «بلا بيانات حضور» صارت حالةً معلَنة، والصارم
/// يمنع الاحتساب بدل أن يدفع كاملاً، والساعات لا تتجاوز 100%.
/// </summary>
public class AttendanceSalaryLinkTests
{
    [Fact]
    public void Lenient_WithData_MatchesPrePolicyFormula()
    {
        var d = AttendanceSalaryLink.Evaluate(AttendanceSalaryLink.Lenient, workDays: 26, absentDays: 2, workedHours: 0);

        Assert.True(d.Include);
        Assert.Equal(24m / 26m, d.Factor);
        Assert.Null(d.Note);   // حضور طبيعي لا يحتاج إعلاناً
    }

    [Fact]
    public void Lenient_WithoutData_PaysInFull_ButDeclaresIt()
    {
        var d = AttendanceSalaryLink.Evaluate(AttendanceSalaryLink.Lenient, 0, 0, 0);

        Assert.True(d.Include);
        Assert.Equal(1m, d.Factor);          // نفس سلوك ما قبل السياسة
        Assert.NotNull(d.Note);              // لكنه لم يعد صامتاً
        Assert.Contains("كاملاً", d.Note);
    }

    [Fact]
    public void Strict_WithoutData_DoesNotCalculateAtAll()
    {
        var d = AttendanceSalaryLink.Evaluate(AttendanceSalaryLink.Strict, 0, 0, 0);

        Assert.False(d.Include);
        Assert.Equal(0m, d.Factor);
        Assert.NotNull(d.Note);
    }

    [Fact]
    public void Strict_WithData_BehavesExactlyLikeLenient()
    {
        var strict = AttendanceSalaryLink.Evaluate(AttendanceSalaryLink.Strict, 26, 4, 0);
        var lenient = AttendanceSalaryLink.Evaluate(AttendanceSalaryLink.Lenient, 26, 4, 0);

        Assert.Equal(lenient.Factor, strict.Factor);
        Assert.True(strict.Include);
    }

    [Fact]
    public void Hours_ProratesByActualHours()
    {
        // 20 يوم عمل × 8 = 160 ساعة متوقّعة، عُملت 120 ⟹ 75%
        var d = AttendanceSalaryLink.Evaluate(AttendanceSalaryLink.Hours, 20, 0, 120m);

        Assert.True(d.Include);
        Assert.Equal(0.75m, d.Factor);
        Assert.Contains("120", d.Note);
    }

    [Fact]
    public void Hours_NeverExceedsFullSalary_OvertimeIsASeparateItem()
    {
        var d = AttendanceSalaryLink.Evaluate(AttendanceSalaryLink.Hours, 20, 0, 400m);

        Assert.Equal(1m, d.Factor);
    }

    [Fact]
    public void Hours_WithoutData_DoesNotCalculate_NoDivisionByZero()
    {
        var d = AttendanceSalaryLink.Evaluate(AttendanceSalaryLink.Hours, 0, 0, 90m);

        Assert.False(d.Include);
    }

    [Fact]
    public void AbsentDaysBeyondWorkDays_ClampToZero_NotNegativeSalary()
    {
        var d = AttendanceSalaryLink.Evaluate(AttendanceSalaryLink.Lenient, 20, 25, 0);

        Assert.Equal(0m, d.Factor);
    }

    [Fact]
    public void UnknownMode_FallsBackToLenient_TheNonBreakingDefault()
    {
        Assert.Equal(AttendanceSalaryLink.Lenient, AttendanceSalaryLink.NormalizeMode(null));
        Assert.Equal(AttendanceSalaryLink.Lenient, AttendanceSalaryLink.NormalizeMode("Nonsense"));
        Assert.Equal(AttendanceSalaryLink.Strict, AttendanceSalaryLink.NormalizeMode("Strict"));
        Assert.Equal(AttendanceSalaryLink.Hours, AttendanceSalaryLink.NormalizeMode("Hours"));
    }

    [Fact]
    public void MonthRowWithZeroWorkDays_CountsAsNoData_NotAsZeroAttendance()
    {
        // صفّ اعتماد موجود بأيام عمل صفر (بلا مناوبة مسنَدة) — القسمة مستحيلة،
        // وادّعاء «صفر حضور» يصفّر راتباً بلا سند.
        Assert.False(AttendanceSalaryLink.HasAttendanceData(0));
        Assert.True(AttendanceSalaryLink.HasAttendanceData(1));
    }
}
