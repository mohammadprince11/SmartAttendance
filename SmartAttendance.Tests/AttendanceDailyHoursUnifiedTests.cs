using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// Issue 11: الساعات المعيارية لليوم مصدرٌ واحد. نمط الحضور «بالساعات» كان يستعمل
/// ثابتاً 8 بينما الأوفرتايم يقرأ الإعداد — فيختلف الافتراضان. الآن الساعات حقلٌ
/// على السياسة تُحمَّل من نفس مفتاح الأوفرتايم.
/// </summary>
public class AttendanceDailyHoursUnifiedTests
{
    [Fact]
    public void HoursMode_DefaultEightHours_BackwardCompatible()
    {
        // الافتراض 8: 140 من (20×8=160) = 0.875 — سلوك ما قبل التغيير.
        var policy = new AttendanceSalaryLink.Policy(AttendanceSalaryLink.Hours, 1m, false);
        var d = AttendanceSalaryLink.Evaluate(policy, workDays: 20, presentDays: 20, absentDays: 0, workedHours: 140m);
        Assert.Equal(0.875m, d.Factor);
    }

    [Fact]
    public void HoursMode_ConfiguredSevenHours_ChangesExpectedHours()
    {
        // ساعات مهيَّأة 7: المتوقّع 20×7=140، فـ140 ساعة عمل = معامل 1 (مقصوص).
        var policy = new AttendanceSalaryLink.Policy(AttendanceSalaryLink.Hours, 1m, false, StandardDailyHours: 7m);
        var d = AttendanceSalaryLink.Evaluate(policy, workDays: 20, presentDays: 20, absentDays: 0, workedHours: 140m);
        Assert.Equal(1m, d.Factor);
    }

    [Fact]
    public void Policy_NonPositiveHours_FallsBackToEight()
    {
        var policy = new AttendanceSalaryLink.Policy(AttendanceSalaryLink.Hours, 1m, false, StandardDailyHours: 0m).Normalized();
        Assert.Equal(8m, policy.StandardDailyHours);
    }

    [Fact]
    public void SameKeyAsOvertime()
    {
        // مفتاح الساعات واحدٌ للاثنين — الحارس ضدّ افتراضين متباعدين.
        Assert.Equal("Payroll.StandardDailyHours", PayrollDivisorPolicy.StandardDailyHoursKey);
    }
}
