using System.IO.Compression;
using System.Text;
using SmartAttendance.Web.Infrastructure.Reports;

namespace SmartAttendance.Tests;

public sealed class ReportExportServiceTests
{
    private static readonly IReadOnlyList<ReportExportService.Column> Columns =
    [
        new("name", "اسم الموظف"),
        new("note", "الملاحظة")
    ];

    [Fact]
    public void Csv_IsUtf8EscapedAndNeutralizesSpreadsheetFormulaInjection()
    {
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["name"] = "محمد, علي", ["note"] = "=HYPERLINK(\"https://invalid\")" },
            new() { ["name"] = "زيدان", ["note"] = "سطر أول\nسطر ثان" }
        };

        var result = ReportExportService.Build("csv", "الموظفون", Columns, rows);
        var preamble = Encoding.UTF8.GetPreamble();
        Assert.Equal("csv", result.Extension);
        Assert.Equal(preamble, result.Bytes[..preamble.Length]);

        var text = Encoding.UTF8.GetString(result.Bytes[preamble.Length..]);
        Assert.Contains("\"محمد, علي\"", text, StringComparison.Ordinal);
        Assert.Contains("\"'=HYPERLINK(\"\"https://invalid\"\")\"", text, StringComparison.Ordinal);
        Assert.Contains("\"سطر أول\nسطر ثان\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Xlsx_IsValidRtlWorkbookWithFrozenHeaderAndSanitizedArabicData()
    {
        var rows = new List<Dictionary<string, string>>
        {
            new() { ["name"] = "  محمد علي  ", ["note"] = "نص\u0001 آمن" }
        };

        var result = ReportExportService.Build("xlsx", "الموظفون", Columns, rows);
        Assert.Equal("xlsx", result.Extension);
        Assert.Equal((byte)'P', result.Bytes[0]);
        Assert.Equal((byte)'K', result.Bytes[1]);

        using var archive = new ZipArchive(new MemoryStream(result.Bytes), ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.NotNull(archive.GetEntry("xl/workbook.xml"));
        Assert.NotNull(archive.GetEntry("xl/styles.xml"));
        var sheet = archive.GetEntry("xl/worksheets/sheet1.xml");
        Assert.NotNull(sheet);
        using var reader = new StreamReader(sheet!.Open(), Encoding.UTF8);
        var xml = reader.ReadToEnd();

        Assert.Contains("rightToLeft=\"1\"", xml, StringComparison.Ordinal);
        Assert.Contains("state=\"frozen\"", xml, StringComparison.Ordinal);
        Assert.Contains("autoFilter", xml, StringComparison.Ordinal);
        Assert.Contains("اسم الموظف", xml, StringComparison.Ordinal);
        Assert.Contains("  محمد علي  ", xml, StringComparison.Ordinal);
        Assert.Contains("نص آمن", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("\u0001", xml, StringComparison.Ordinal);

        using var workbookReader = new StreamReader(archive.GetEntry("xl/workbook.xml")!.Open(), Encoding.UTF8);
        var workbookXml = workbookReader.ReadToEnd();
        Assert.Contains("name=\"الموظفون\"", workbookXml, StringComparison.Ordinal);
    }

    [Fact]
    public void PeopleReportPage_OffersExplicitExcelAndCsvExportsThroughSharedService()
    {
        var root = FindRoot();
        var view = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "PeopleReports", "Index.cshtml"));
        var page = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "PeopleReports", "Index.cshtml.cs"));

        Assert.Contains("handler=Export&format=xlsx", view, StringComparison.Ordinal);
        Assert.Contains("handler=Export&format=csv", view, StringComparison.Ordinal);
        Assert.Contains("ReportExportService.Build(", page, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
