using System.Globalization;
using SmartAttendance.Web.Infrastructure.Imports;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// انحدار مالي: عمود «الراتب الأساسي» بقالب استيراد الموظفين يمرّ من هنا، وأي
/// خطأ قراءة يصير راتباً خاطئاً بالقسيمة — لا خطأ عرض.
/// </summary>
public class ImportAmountParserTests
{
    [Theory]
    [InlineData("500000", 500000)]
    [InlineData("500,000", 500000)]
    [InlineData(" 500000 ", 500000)]
    [InlineData("1500.50", 1500.50)]
    [InlineData("0", 0)]
    public void Reads_plain_and_grouped_amounts(string input, decimal expected)
    {
        Assert.True(ImportAmountParser.TryParse(input, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("٥٠٠٠٠٠", 500000)]          // أرقام عربية-هندية
    [InlineData("۵۰۰۰۰۰", 500000)]          // أرقام فارسية
    [InlineData("٥٠٠٬٠٠٠", 500000)]         // بفاصلة آلاف عربية
    [InlineData("١٥٠٠٫٥", 1500.5)]          // بفاصلة عشرية عربية
    public void Reads_eastern_digits(string input, decimal expected)
    {
        Assert.True(ImportAmountParser.TryParse(input, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("500,000 د.ع", 500000)]
    [InlineData("$1,250.75", 1250.75)]
    [InlineData("500 000", 500000)]     // مسافة غير فاصلة من إكسل
    public void Ignores_currency_and_excel_spacing(string input, decimal expected)
    {
        Assert.True(ImportAmountParser.TryParse(input, out var actual));
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// الفخّ الذي كسر النسخة الأولى: إسقاط الفاصلة العشرية العربية بدل تحويلها
    /// كان يقرأ ١٫٥ رقماً صحيحاً ١٥ — عشرة أضعاف الراتب، بصمت.
    /// </summary>
    [Fact]
    public void Arabic_decimal_separator_is_not_dropped()
    {
        Assert.True(ImportAmountParser.TryParse("١٫٥", out var actual));
        Assert.Equal(1.5m, actual);
        Assert.NotEqual(15m, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("راتب")]
    [InlineData("-")]
    [InlineData("د.ع")]
    public void Rejects_blank_and_non_numeric(string? input)
    {
        Assert.False(ImportAmountParser.TryParse(input, out var actual));
        Assert.Equal(0m, actual);
    }

    [Fact]
    public void Negative_amount_parses_so_caller_can_reject_it()
    {
        // المحلّل يقرأ الإشارة؛ رفض السالب قرار المستورِد لا القارئ.
        Assert.True(ImportAmountParser.TryParse("-100", out var actual));
        Assert.Equal(-100m, actual);
    }

    /// <summary>
    /// الحارس الأهم: النتيجة لا تتغيّر بتغيّر ثقافة الجهاز. بثقافة تجعل الفاصلة
    /// فاصلةً عشرية، «1.5» يجب أن تبقى واحداً ونصفاً لا خمسة عشر.
    /// </summary>
    [Fact]
    public void Result_does_not_depend_on_machine_culture()
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.True(ImportAmountParser.TryParse("1.5", out var actual));
            Assert.Equal(1.5m, actual);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
