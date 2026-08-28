using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حارس انحدار لطباعة قسيمة الراتب. الطباعة المباشرة للنافذة تضمّن قوقعة ZYNORA
/// في مستند الطباعة، فكانت خلفية الثيم الداكنة تظهر حول القسيمة في المعاينة والورق.
/// </summary>
public sealed class PayslipPrintIsolationGuardTests
{
    private static string PayslipPage() =>
        RazorPageAssetReader.ReadWithLinkedPageCss("Payroll", "PayslipInquiry.cshtml");

    [Fact]
    public void PrintButtonUsesAnIsolatedDocument_NotTheApplicationWindow()
    {
        var page = PayslipPage();

        Assert.Contains("onclick=\"printPayslip()\"", page);
        Assert.Contains("window.printPayslip = function", page);
        Assert.Contains("document.createElement('iframe')", page);
        Assert.Contains("slip.outerHTML", page);
        Assert.DoesNotContain("onclick=\"window.print()\"", page);
    }

    [Fact]
    public void PrintedSlipHasNoOuterFrameOrThemeUnderlay()
    {
        var page = PayslipPage();

        Assert.Matches(
            new Regex(@"@media\s+print[\s\S]*?\.ps\s*\{[^}]*border\s*:\s*0\s*!important", RegexOptions.CultureInvariant),
            page);
        Assert.DoesNotContain("ps-print-bg", page);
        Assert.Contains("background:var(--zy-white)!important", page);
    }

    [Fact]
    public void IncomeAndDeductionTablesUseTwoColumnsWithoutDays()
    {
        var page = PayslipPage();

        Assert.DoesNotContain(">أيام</th>", page);
        Assert.DoesNotContain("class=\"days\"", page);
        Assert.DoesNotContain("var psPaid", page);
        Assert.Equal(2, Regex.Matches(page, "<col class=\"amount-col\" />").Count);
    }
}
