using SmartAttendance.Web.Infrastructure.Imports;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// انحدار: القالب المترجَم للعربية كان يُرفض كلّه بـ«Missing required columns»
/// رغم سلامة بياناته. هذه الاختبارات تثبّت قبول الترويسات العربية **وعدم**
/// كسر الإنجليزية والحقول المخصّصة.
/// </summary>
public class ImportHeaderAliasesTests
{
    /// <summary>الترويسات الفعلية لقالب المستخدم — بنجمة «مطلوب» وكلها عربية.</summary>
    [Theory]
    [InlineData("رقم الموظف *", "EmployeeNo")]
    [InlineData("الاسم الكامل *", "FullName")]
    [InlineData("اسم الشركة *", "CompanyName")]
    [InlineData("اسم موقع العمل *", "WorkLocationName")]
    [InlineData("اسم القسم *", "DepartmentName")]
    [InlineData("المسمى الوظيفي *", "PositionName")]
    [InlineData("تاريخ التعيين *", "HireDate")]
    [InlineData("الراتب الأساسي", "BasicSalary")]
    [InlineData("رقم الموظف للمدير المباشر", "DirectManagerEmployeeNo")]
    public void Arabic_header_maps_to_canonical_english(
        string arabic,
        string english)
    {
        Assert.Equal(
            ImportHeaderAliases.Canonicalize(english),
            ImportHeaderAliases.Canonicalize(arabic));
    }

    /// <summary>
    /// الفخّ: «اسم الشركة المرجعي» يجب ألّا يُطابق «اسم الشركة» — وإلا صار
    /// العمود المرجعي عمودَ بيانات وأفسد كل صفّ.
    /// </summary>
    [Theory]
    [InlineData("اسم الشركة المرجعي", "Ref Company Name")]
    [InlineData("رمز القسم المرجعي", "Ref Department Code")]
    [InlineData("شركة المسمى الوظيفي المرجعية", "Ref Position Company")]
    public void Reference_headers_do_not_collide_with_data_headers(
        string arabicReference,
        string englishReference)
    {
        var reference = ImportHeaderAliases.Canonicalize(arabicReference);

        Assert.Equal(
            ImportHeaderAliases.Canonicalize(englishReference),
            reference);

        Assert.NotEqual(
            ImportHeaderAliases.Canonicalize("اسم الشركة"),
            reference);

        // البادئة "ref" هي ما يعتمده المحرك لتجاهل الأعمدة المرجعية.
        Assert.StartsWith("ref", reference, System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("EmployeeNo")]
    [InlineData("employee no")]
    [InlineData("Employee_No")]
    [InlineData("EMPLOYEENO")]
    public void English_headers_keep_working(string header)
    {
        Assert.Equal(
            "employeeno",
            ImportHeaderAliases.Canonicalize(header));
    }

    [Fact]
    public void Unknown_header_passes_through_normalized()
    {
        // الحقول المخصّصة الديناميكية تعتمد على مرور غير المعروف كما هو.
        Assert.Equal(
            "حقلمخصص",
            ImportHeaderAliases.Canonicalize("حقل مخصص"));

        Assert.Equal(
            "shirtsize",
            ImportHeaderAliases.Canonicalize("Shirt Size"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("***")]
    public void Blank_and_symbol_only_headers_normalize_to_empty(string? header)
    {
        Assert.Equal(
            string.Empty,
            ImportHeaderAliases.Canonicalize(header));
    }

    /// <summary>
    /// كل عمود مطلوب بالمحرك له مرادف عربي — وإلا بقي القالب المترجَم مرفوضاً
    /// جزئياً وهو أسوأ من الرفض الكامل لأنه يمرّ بصمت ناقصاً.
    /// </summary>
    [Theory]
    [InlineData("رقم الموظف")]
    [InlineData("الاسم الكامل")]
    [InlineData("اسم الشركة")]
    [InlineData("اسم موقع العمل")]
    [InlineData("اسم القسم")]
    [InlineData("المسمى الوظيفي")]
    [InlineData("تاريخ التعيين")]
    public void Every_required_column_has_an_arabic_alias(string arabic)
    {
        var canonical = ImportHeaderAliases.Canonicalize(arabic);

        // لو لم يكن مرادفاً معروفاً لعاد النصّ العربي مطبَّعاً كما هو.
        Assert.Matches("^[a-z0-9]+$", canonical);
    }
}
