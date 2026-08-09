using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حارس الخصم المزدوج (Phase 18): الحدث الواحد (تأخّر/غياب) قد يمسّ ثلاث قنوات
/// <b>مستقلّة</b>: تنسيب الحضور (بالإجمالي)، وعقوبة تأديبية، وخصم يدويّ. المحرك
/// يجب أن يعدّ كلّاً <b>مرّة واحدة</b> بنوعها (<c>Kind</c>) الخاصّ، فلا يُخصم الحدث
/// مرّتين إلا بسياسة شركةٍ صريحة (إعداد قناتين).
///
/// يثبّت هذا الاختبار الفصل على مستوى نموذج القسيمة العام (<see cref="PayrollRunStore.PayrollLine"/>):
/// أنواع المكوّنات لا تتداخل، وكل قناة تدخل الصافي بندٌ واحد.
/// </summary>
public class PayrollDoubleDeductionTests
{
    private static PayrollRunStore.Component Earn(string name, decimal amt, string kind) =>
        new() { ItemName = name, Amount = amt, IsAddition = true, Kind = kind };

    private static PayrollRunStore.Component Deduct(string name, decimal amt, string kind) =>
        new() { ItemName = name, Amount = amt, IsAddition = false, Kind = kind };

    [Fact]
    public void LateEvent_ThreeChannels_EachCountedOnce_NoOverlap()
    {
        // غياب يومين لموظف: (1) تنسيب الحضور خفّض الأساسي (بند Basic واحد)،
        // (2) عقوبة تأديبية (بند Penalty واحد)، (3) خصم يدويّ (بند Deduction واحد).
        var line = new PayrollRunStore.PayrollLine
        {
            GrossSalary = 1_846_153.85m,   // أساسي منسَّب + علاوات (قناة الحضور)
            TaxAmount = 0m,
            GosiEmployee = 0m,
            OtherDeductions = 50_000m + 30_000m,   // عقوبة + خصم يدويّ
            Components =
            {
                Earn("الراتب الأساسي", 369_230.77m, "Basic"),
                Earn("علاوات", 1_476_923.08m, "Allowance"),
                Deduct("خصم مخالفة تأخّر", 50_000m, "Penalty"),
                Deduct("خصم يدويّ", 30_000m, "Deduction"),
            }
        };

        // كل قناة نوعٌ مستقلّ — لا مكوّن يحمل نوعين، فلا يُحسب الحدث بقناتين بالخطأ.
        Assert.Single(line.Components, c => c.Kind == "Penalty");
        Assert.Single(line.Components, c => c.Kind == "Deduction");
        Assert.Single(line.Components, c => c.Kind == "Basic");

        // تنسيب الحضور يظهر بالإجمالي فقط، لا كخصمٍ مكرَّر ضمن الاقتطاعات.
        Assert.DoesNotContain(line.Deductions, c => c.Kind is "Basic" or "Allowance");

        // مجموع الاقتطاعات = العقوبة + الخصم اليدويّ (كلٌّ مرّة)، لا ضعفهما.
        Assert.Equal(80_000m, line.Deductions.Sum(c => c.Amount));
        Assert.Equal(80_000m, line.TotalDeductions);
    }

    [Fact]
    public void AttendanceProration_IsInGross_NotADeductionLine()
    {
        // قناة الحضور أثرُها بتخفيض الإجمالي (بند Basic أقلّ) لا ببندِ خصمٍ مستقلّ —
        // وإلا خُصم الغياب مرّتين: مرّة بالتنسيب ومرّة كخصم.
        var line = new PayrollRunStore.PayrollLine
        {
            GrossSalary = 1_846_153.85m,
            OtherDeductions = 0m,
            Components = { Earn("الراتب الأساسي", 369_230.77m, "Basic") },
        };

        Assert.Empty(line.Deductions);
        Assert.Equal(0m, line.TotalDeductions);
        Assert.All(line.Earnings, c => Assert.True(c.IsAddition));
    }
}
