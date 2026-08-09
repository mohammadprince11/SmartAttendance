using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// خضوع العلاوة للضريبة/الضمان <b>لكل علاوة</b> (Phase 2): المكوّنان الجديدان
/// <c>TaxableAllowances</c>/<c>GosiAllowances</c> يحملان مجموع العلاوات المؤهَّلة فقط،
/// فيمكن للملف أن يبدّل عضويته من «العلاوات (الكل)» إليهما.
///
/// التوافق الخلفيّ: العضوية الافتراضية ما زالت «Allowances» (الكل)، فلا يتغيّر وعاء
/// أي ملف قائم حتى يبدّله المستخدم.
/// </summary>
public class PerAllowanceTaxGosiTests
{
    // موظف: أساسي 400,000، علاوتان: سكن 1,500,000 (خاضع/مؤهَّل)، هاتف 100,000 (غير خاضع/غير مؤهَّل).
    private static SalaryBaseComposer.Amounts Sample() => new()
    {
        Basic = 400_000m,
        Allowances = 1_600_000m,          // الكل
        TaxableAllowances = 1_500_000m,   // السكن فقط
        GosiAllowances = 1_500_000m,      // السكن فقط
        Gross = 2_000_000m
    };

    [Fact]
    public void DefaultMembership_UsesAllAllowances_Unchanged()
    {
        // الافتراض «Allowances» = كل العلاوات (السلوك القائم).
        var taxBase = SalaryBaseComposer.Compose(Sample(), SalaryBaseComposer.DefaultTaxMembers);
        Assert.Equal(2_000_000m, taxBase);   // 400,000 + 1,600,000 (كل العلاوات)
    }

    [Fact]
    public void TaxableAllowancesMembership_HonorsPerAllowanceTaxability()
    {
        // ملف يبدّل عضويته لـ«الخاضعة فقط»: الهاتف غير الخاضع يخرج من الوعاء.
        var taxBase = SalaryBaseComposer.Compose(Sample(), new[] { "Basic", "TaxableAllowances" });
        Assert.Equal(1_900_000m, taxBase);   // 400,000 + 1,500,000 (السكن فقط)
    }

    [Fact]
    public void GosiAllowancesMembership_HonorsPerAllowanceEligibility()
    {
        var gosiBase = SalaryBaseComposer.Compose(Sample(), new[] { "Basic", "GosiAllowances" });
        Assert.Equal(1_900_000m, gosiBase);  // 400,000 + 1,500,000 (المؤهَّل فقط)
    }

    [Fact]
    public void NewComponentKeys_AreKnown()
    {
        var known = SalaryBaseComposer.Components.Select(c => c.Key).ToHashSet();
        Assert.Contains("TaxableAllowances", known);
        Assert.Contains("GosiAllowances", known);
    }
}
