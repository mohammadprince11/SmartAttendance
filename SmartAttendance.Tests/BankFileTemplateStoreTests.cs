using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات نقية لبناء ملف البنك <see cref="BankFileTemplateStore"/> — بلا قاعدة بيانات.
/// بؤرتها التنسيق المالي الفعلي المُرسَل للبنك: الأعمدة، الفواصل، الرؤوس المخصصة،
/// التهريب (quoting)، خريطة قيم الحقول، وعلَم «قابل للتحويل». عيبٌ هنا = ملف بنك خاطئ.
/// </summary>
public class BankFileTemplateStoreTests
{
    private static BankFileTemplateStore.Template Tpl(
        string columns, string delimiter = "Comma", bool header = true, string? headers = null) => new()
    {
        Delimiter = delimiter,
        IncludeHeader = header,
        ColumnsCsv = columns,
        HeadersCsv = headers
    };

    private static PayrollRunStore.BankFileRow Row(
        string no = "E1", string name = "أحمد", string method = "تحويل", string bank = "الرافدين",
        string branch = "الكرادة", string iban = "IQ12", string card = "", decimal net = 1000m) => new()
    {
        EmployeeNo = no, EmployeeName = name, PaymentMethod = method, BankName = bank,
        BankBranch = branch, Iban = iban, CardNo = card, NetSalary = net
    };

    /// <summary>يفصل المحتوى إلى أسطر غير فارغة بصرف النظر عن نوع فاصل السطر.</summary>
    private static string[] Lines(string content) =>
        content.Replace("\r\n", "\n").Split('\n', System.StringSplitOptions.RemoveEmptyEntries);

    // ---------------- تحليل الأعمدة والرؤوس ----------------

    [Fact]
    public void Columns_SplitTrimAndDropEmpty()
    {
        var t = Tpl(" no , name ,, net ");
        Assert.Equal(new[] { "no", "name", "net" }, t.Columns);
    }

    [Fact]
    public void Headers_SplitOnPipe_KeepEmptySlots()
    {
        var t = Tpl("no,name,net", headers: "الرقم|الاسم||");
        Assert.Equal(new[] { "الرقم", "الاسم", "", "" }, t.Headers);
    }

    [Fact]
    public void HeaderFor_UsesCustomWhenPresent_ElseDefaultLabel()
    {
        var t = Tpl("no,name,net", headers: "الرقم|الاسم||");
        Assert.Equal("الرقم", t.HeaderFor(0, "no"));            // مخصص
        Assert.Equal("الاسم", t.HeaderFor(1, "name"));          // مخصص
        Assert.Equal("صافي الراتب", t.HeaderFor(2, "net"));     // خانة فارغة ⟶ الافتراضي
        Assert.Equal("قابل للتحويل", t.HeaderFor(9, "payable")); // خارج المدى ⟶ الافتراضي
    }

    [Fact]
    public void LabelOf_And_DelimiterChar_FallBackForUnknownKeys()
    {
        Assert.Equal("الآيبان", BankFileTemplateStore.LabelOf("iban"));
        Assert.Equal("mystery", BankFileTemplateStore.LabelOf("mystery")); // غير معروف ⟶ المفتاح نفسه
        Assert.Equal(",", BankFileTemplateStore.DelimiterChar("Comma"));
        Assert.Equal(";", BankFileTemplateStore.DelimiterChar("Semicolon"));
        Assert.Equal("\t", BankFileTemplateStore.DelimiterChar("Tab"));
        Assert.Equal(",", BankFileTemplateStore.DelimiterChar("Unknown")); // افتراضي
    }

    // ---------------- بناء المحتوى: الترويسة ----------------

    [Fact]
    public void BuildContent_IncludesHeaderRow_WithDefaultLabels()
    {
        var content = BankFileTemplateStore.BuildContent(Tpl("no,name,net"), new[] { Row() });
        var lines = Lines(content);
        Assert.Equal(2, lines.Length);
        Assert.Equal("الرقم الوظيفي,اسم الموظف,صافي الراتب", lines[0]);
    }

    [Fact]
    public void BuildContent_OmitsHeaderRow_WhenDisabled()
    {
        var content = BankFileTemplateStore.BuildContent(Tpl("no,name,net", header: false), new[] { Row() });
        var lines = Lines(content);
        Assert.Single(lines);
        Assert.StartsWith("E1,أحمد,", lines[0]);
    }

    [Fact]
    public void BuildContent_UsesCustomHeaders()
    {
        var content = BankFileTemplateStore.BuildContent(
            Tpl("no,name,net", headers: "الرقم|الاسم|الصافي"), new[] { Row() });
        Assert.Equal("الرقم,الاسم,الصافي", Lines(content)[0]);
    }

    // ---------------- بناء المحتوى: القيم والخريطة ----------------

    [Fact]
    public void BuildContent_MapsEveryFieldKey()
    {
        var row = Row(no: "1001", name: "سالم", method: "تحويل", bank: "الرشيد",
            branch: "المنصور", iban: "IQ99", card: "5555", net: 1234.5m);
        var content = BankFileTemplateStore.BuildContent(
            Tpl("no,name,method,bank,branch,iban,card,net,payable", header: false), new[] { row });
        Assert.Equal("1001,سالم,تحويل,الرشيد,المنصور,IQ99,5555,1234.50,نعم", Lines(content)[0]);
    }

    [Fact]
    public void BuildContent_FormatsNetWithTwoDecimals_InvariantCulture()
    {
        var content = BankFileTemplateStore.BuildContent(
            Tpl("net", header: false), new[] { Row(net: 1000000m) });
        Assert.Equal("1000000.00", Lines(content)[0]); // نقطة عشرية لا فاصلة مهما كانت ثقافة الخادم
    }

    [Theory]
    [InlineData("IQ12", "", "نعم")]   // آيبان فقط
    [InlineData("", "4321", "نعم")]   // بطاقة فقط
    [InlineData("IQ12", "4321", "نعم")] // كلاهما
    [InlineData("", "", "لا")]        // لا شيء ⟶ غير قابل للتحويل
    [InlineData("   ", "  ", "لا")]   // فراغات فقط ⟶ غير قابل للتحويل
    public void BuildContent_PayableFlag_ReflectsIbanOrCard(string iban, string card, string expected)
    {
        var content = BankFileTemplateStore.BuildContent(
            Tpl("payable", header: false), new[] { Row(iban: iban, card: card) });
        Assert.Equal(expected, Lines(content)[0]);
    }

    // ---------------- الفواصل ----------------

    [Fact]
    public void BuildContent_HonorsSemicolonAndTabDelimiters()
    {
        var semi = BankFileTemplateStore.BuildContent(
            Tpl("no,name", "Semicolon", header: false), new[] { Row() });
        Assert.Equal("E1;أحمد", Lines(semi)[0]);

        var tab = BankFileTemplateStore.BuildContent(
            Tpl("no,name", "Tab", header: false), new[] { Row() });
        Assert.Equal("E1\tأحمد", Lines(tab)[0]);
    }

    // ---------------- التهريب (quoting) ----------------

    [Fact]
    public void BuildContent_QuotesValueContainingDelimiter()
    {
        var content = BankFileTemplateStore.BuildContent(
            Tpl("name", header: false), new[] { Row(name: "أحمد, سالم") });
        Assert.Equal("\"أحمد, سالم\"", Lines(content)[0]);
    }

    [Fact]
    public void BuildContent_EscapesEmbeddedQuotesByDoubling()
    {
        var content = BankFileTemplateStore.BuildContent(
            Tpl("name", header: false), new[] { Row(name: "شركة \"النور\"") });
        Assert.Equal("\"شركة \"\"النور\"\"\"", Lines(content)[0]);
    }

    [Fact]
    public void BuildContent_CommaValueNotQuoted_WhenDelimiterIsSemicolon()
    {
        // قيمة فيها فاصلة لكن الفاصل فاصلة منقوطة ⟹ لا حاجة للتهريب.
        var content = BankFileTemplateStore.BuildContent(
            Tpl("name", "Semicolon", header: false), new[] { Row(name: "أحمد, سالم") });
        Assert.Equal("أحمد, سالم", Lines(content)[0]);
    }

    // ---------------- عدة صفوف ----------------

    [Fact]
    public void BuildContent_EmitsOneLinePerRow_PlusHeader()
    {
        var rows = new[] { Row(no: "1"), Row(no: "2"), Row(no: "3") };
        var content = BankFileTemplateStore.BuildContent(Tpl("no,name"), rows);
        Assert.Equal(4, Lines(content).Length); // ترويسة + 3 صفوف
    }

    [Fact]
    public void BuildContent_NoRows_YieldsHeaderOnly()
    {
        var content = BankFileTemplateStore.BuildContent(Tpl("no,name"), System.Array.Empty<PayrollRunStore.BankFileRow>());
        Assert.Single(Lines(content));
    }
}
