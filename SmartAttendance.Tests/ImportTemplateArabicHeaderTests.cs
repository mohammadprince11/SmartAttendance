using SmartAttendance.Web.Infrastructure.Imports;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// انحدار الرحلة الكاملة: القالب يُنزَّل بترويسات عربية، ويجب أن يُقبل عند رفعه.
/// أي ترويسة يكتبها المولِّد ولا تعرفها خريطة المرادفات = قالبٌ يرفض نفسه.
/// </summary>
public class ImportTemplateArabicHeaderTests
{
    /// <summary>
    /// الترويسات العربية التي يكتبها مولّد القالب (بما فيها الأعمدة المرجعية)،
    /// مقرونةً بمفاتيحها الإنجليزية المعيارية.
    /// </summary>
    public static TheoryData<string, string> GeneratedHeaders() => new()
    {
        { "رقم الموظف", "EmployeeNo" },
        { "الاسم الكامل", "FullName" },
        { "اسم الشركة", "CompanyName" },
        { "رمز الشركة", "CompanyCode" },
        { "اسم موقع العمل", "WorkLocationName" },
        { "رمز موقع العمل", "WorkLocationCode" },
        { "اسم القسم", "DepartmentName" },
        { "رمز القسم", "DepartmentCode" },
        { "المسمى الوظيفي", "PositionName" },
        { "رمز المسمى الوظيفي", "PositionCode" },
        { "تاريخ التعيين", "HireDate" },
        { "رقم الهوية الوطنية", "NationalId" },
        { "رقم الهاتف", "Phone" },
        { "البريد الإلكتروني", "Email" },
        { "تاريخ الميلاد", "BirthDate" },
        { "الجنس", "Gender" },
        { "الحالة الاجتماعية", "MaritalStatus" },
        { "الجنسية", "Nationality" },
        { "البلد", "Country" },
        { "نوع العقد", "ContractType" },
        { "تاريخ انتهاء العقد", "ContractEndDate" },
        { "الحالة الوظيفية", "EmploymentStatus" },
        { "فعال", "IsActive" },
        { "رقم الموظف للمدير المباشر", "DirectManagerEmployeeNo" },
        { "الراتب الأساسي", "BasicSalary" },
        { "الاسم الأول (عربي)", "FirstName" },
        { "الاسم الثاني (عربي)", "SecondName" },
        { "الاسم الثالث (عربي)", "ThirdName" },
        { "اللقب (عربي)", "LastName" },
        { "الاسم الأول (إنجليزي)", "FirstNameEn" },
        { "الاسم الثاني (إنجليزي)", "SecondNameEn" },
        { "الاسم الثالث (إنجليزي)", "ThirdNameEn" },
        { "اللقب (إنجليزي)", "LastNameEn" },
        { "مواطن", "IsCitizen" },
        { "رقم جواز السفر", "PassportNo" },
        { "اسم الكفيل", "SponsorName" },
        { "الديانة", "Religion" },
        { "البلد الأم", "MotherCountry" },
        { "المدينة الأم", "MotherCity" },
        { "تاريخ المباشرة الفعلية", "JoiningDate" },
        { "نوع الدوام", "WorkType" },
        { "الدرجة الوظيفية", "JobGrade" },
        { "امتداد الهاتف", "PhoneExtension" },
        { "البريد الشخصي", "PersonalEmail" },
        { "اسم الشركة المرجعي", "Ref Company Name" },
        { "رمز الشركة المرجعي", "Ref Company Code" },
        { "اسم موقع العمل المرجعي", "Ref Work Location Name" },
        { "رمز موقع العمل المرجعي", "Ref Work Location Code" },
        { "شركة موقع العمل المرجعية", "Ref Work Location Company" },
        { "اسم القسم المرجعي", "Ref Department Name" },
        { "رمز القسم المرجعي", "Ref Department Code" },
        { "شركة القسم المرجعية", "Ref Department Company" },
        { "اسم المسمى الوظيفي المرجعي", "Ref Position Name" },
        { "رمز المسمى الوظيفي المرجعي", "Ref Position Code" },
        { "شركة المسمى الوظيفي المرجعية", "Ref Position Company" }
    };

    [Theory]
    [MemberData(nameof(GeneratedHeaders))]
    public void Downloaded_arabic_header_is_accepted_on_upload(
        string arabicHeader,
        string canonicalKey)
    {
        Assert.Equal(
            ImportHeaderAliases.Canonicalize(canonicalKey),
            ImportHeaderAliases.Canonicalize(arabicHeader));
    }

    /// <summary>
    /// الأعمدة المطلوبة تُكتب بنجمة «*» ملحقة بالترويسة — يجب أن تبقى مقبولة.
    /// </summary>
    [Theory]
    [InlineData("رقم الموظف *", "EmployeeNo")]
    [InlineData("تاريخ التعيين *", "HireDate")]
    [InlineData("المسمى الوظيفي *", "PositionName")]
    public void Required_star_suffix_does_not_break_matching(
        string headerWithStar,
        string canonicalKey)
    {
        Assert.Equal(
            ImportHeaderAliases.Canonicalize(canonicalKey),
            ImportHeaderAliases.Canonicalize(headerWithStar));
    }

    /// <summary>
    /// لا ترويستان مختلفتان تنهاران لمفتاح واحد — تصادمٌ كهذا يخلط عمودين
    /// ببعضهما ويفسد كل صفّ بصمت.
    /// </summary>
    [Fact]
    public void No_two_generated_headers_collide()
    {
        var canonical = GeneratedHeaders()
            .Select(row => ImportHeaderAliases.Canonicalize((string)row[0]))
            .ToList();

        Assert.Equal(canonical.Count, canonical.Distinct().Count());
    }
}
