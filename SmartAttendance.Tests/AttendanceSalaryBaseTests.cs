using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات وعاء الحضور (الموظف الذهبي TEST-PAY-001): أساسي 400,000 + علاوات
/// 1,600,000 = وعاء حضور 2,000,000.
///
/// جوهر التصحيح: الغياب يجب أن يمسّ <b>الأساسي والعلاوات المستحقّة معاً</b> لا
/// الأساسي وحده. والتوافق الخلفيّ: العلاوة الثابتة (غير الحسّاسة) تبقى كاملة،
/// فالافتراض القديم (كل العلاوات ثابتة) يعيد أرقام المحرك السابق حرفياً.
/// </summary>
public class AttendanceSalaryBaseTests
{
    private static AttendanceSalaryBase.EarningComponent Basic(decimal amount) => new(amount, true);
    private static AttendanceSalaryBase.EarningComponent Sensitive(decimal amount) => new(amount, true);
    private static AttendanceSalaryBase.EarningComponent Fixed(decimal amount) => new(amount, false);

    // معامل السيناريو الثاني: 24 من 26 يوماً (غياب يومين).
    private static readonly decimal AbsenceFactor = 24m / 26m;

    [Fact]
    public void Baseline_FullAttendance_FixedGrossUnchanged()
    {
        // حضور كامل (معامل 1): وعاء الحضور والاستحقاق كلاهما 2,000,000 بلا اقتطاع.
        var components = new[] { Basic(400_000m), Sensitive(1_600_000m) };

        Assert.Equal(2_000_000m, AttendanceSalaryBase.Base(components));
        Assert.Equal(2_000_000m, AttendanceSalaryBase.AttendanceAdjusted(components, 1m));
    }

    [Fact]
    public void Absence_AffectsBothBasicAndEligibleAllowances()
    {
        // السيناريو الثاني — الاختبار المفصليّ: غياب يومين يخصم من الأساسي والعلاوة.
        var components = new[] { Basic(400_000m), Sensitive(1_600_000m) };

        Assert.Equal(2_000_000m, AttendanceSalaryBase.Base(components));

        // 400,000 × 24/26 = 369,230.77 · 1,600,000 × 24/26 = 1,476,923.08
        Assert.Equal(369_230.77m, AttendanceSalaryBase.AdjustComponent(components[0], AbsenceFactor));
        Assert.Equal(1_476_923.08m, AttendanceSalaryBase.AdjustComponent(components[1], AbsenceFactor));
        Assert.Equal(1_846_153.85m, AttendanceSalaryBase.AttendanceAdjusted(components, AbsenceFactor));
    }

    [Fact]
    public void Absence_OldBasicOnlyBehavior_DiffersFromNew()
    {
        // إثباتٌ صريح أن السلوك الجديد ≠ القديم: القديم كان يخصم من الأساسي فقط.
        var newModel = new[] { Basic(400_000m), Sensitive(1_600_000m) };
        var oldModel = new[] { Basic(400_000m), Fixed(1_600_000m) };

        var oldResult = AttendanceSalaryBase.AttendanceAdjusted(oldModel, AbsenceFactor);
        var newResult = AttendanceSalaryBase.AttendanceAdjusted(newModel, AbsenceFactor);

        Assert.Equal(1_969_230.77m, oldResult);   // 369,230.77 + 1,600,000 كاملة
        Assert.Equal(1_846_153.85m, newResult);
        Assert.NotEqual(oldResult, newResult);
    }

    [Fact]
    public void FixedAllowance_ProratesHousingButNotPhone()
    {
        // السيناريو الثالث: سكن (حسّاس) يُنسَّب، هاتف (ثابت) يبقى كاملاً.
        var components = new[]
        {
            Basic(400_000m),          // 369,230.77
            Sensitive(1_500_000m),    // سكن → 1,384,615.38
            Fixed(100_000m)           // هاتف → 100,000 كاملاً
        };

        Assert.Equal(1_900_000m, AttendanceSalaryBase.Base(components));   // الحسّاس فقط
        Assert.Equal(369_230.77m, AttendanceSalaryBase.AdjustComponent(components[0], AbsenceFactor));
        Assert.Equal(1_384_615.38m, AttendanceSalaryBase.AdjustComponent(components[1], AbsenceFactor));
        Assert.Equal(100_000m, AttendanceSalaryBase.AdjustComponent(components[2], AbsenceFactor));
        Assert.Equal(1_853_846.15m, AttendanceSalaryBase.AttendanceAdjusted(components, AbsenceFactor));
    }

    [Fact]
    public void AllFixedAllowances_ReproducesLegacyBasicOnlyProration()
    {
        // التوافق الخلفيّ: كل العلاوات ثابتة (الافتراض) ⟹ الأساسي وحده يُنسَّب.
        var components = new[] { Basic(400_000m), Fixed(1_600_000m) };

        var expectedLegacy = decimal.Round(400_000m * AbsenceFactor, 2) + 1_600_000m;
        Assert.Equal(expectedLegacy, AttendanceSalaryBase.AttendanceAdjusted(components, AbsenceFactor));
    }

    [Fact]
    public void Base_IgnoresFixedComponents()
    {
        var components = new[] { Basic(400_000m), Fixed(1_600_000m) };
        Assert.Equal(400_000m, AttendanceSalaryBase.Base(components));
    }
}
