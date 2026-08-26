using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// Phase 5/32 (Design QA): شاشة إعدادات المسير يجب ألّا تصف صيغةً تخالف المحرك.
/// نمط الحضور «بالساعات» كان يعرض «× 8» ثابتاً رغم أن الساعات المعيارية صارت
/// قابلة للتهيئة (Issue 11) — فالنصّ يجب أن يستعمل <c>Model.StandardDailyHours</c>.
/// وصفوف سياسات الأوعية تلتفّ على الجوال (flex-wrap) فلا تتجاوز العرض.
/// </summary>
public class PayrollSettingsUiTests
{
    private static string Page() =>
        RazorPageAssetReader.ReadWithLinkedPageCss("Payroll", "Settings.cshtml");

    [Fact]
    public void HoursMode_Copy_UsesConfiguredHours_NotHardcodedEight()
    {
        var page = Page();
        // لا «أيام العمل × 8)» ثابتة بعد الآن.
        Assert.DoesNotMatch(new Regex(@"أيام العمل × 8\)"), page);
        // يستعمل القيمة المهيَّأة.
        Assert.Contains("أيام العمل × {Model.StandardDailyHours", page);
    }

    [Fact]
    public void BasePolicyRows_WrapOnNarrowViewports()
    {
        var page = Page();
        // الصفوف تلتفّ فلا تُجبر عمودين بعرضٍ يتجاوز الجوال.
        Assert.Contains("flex-wrap:wrap", page);
    }
}
