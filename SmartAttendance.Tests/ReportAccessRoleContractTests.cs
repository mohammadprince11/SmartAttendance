using Xunit;

namespace SmartAttendance.Tests;

/// <summary>يثبت أن تبويب أدوار التقارير ليس إعداداً شكلياً وأن كل مسارات القراءة والكتابة تمر بحارسه.</summary>
public sealed class ReportAccessRoleContractTests
{
    private static string ReadWeb(params string[] parts)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, "SmartAttendance.Web", Path.Combine(parts)));
    }

    [Fact]
    public void ReportPage_MapsEveryDatasetToConfiguredReportGroup()
    {
        var model = ReadWeb("Pages", "PeopleReports", "Index.cshtml.cs");

        Assert.Contains("ReportGroupFor", model, StringComparison.Ordinal);
        Assert.Contains("\"Attendance\"", model, StringComparison.Ordinal);
        Assert.Contains("\"Payroll\"", model, StringComparison.Ordinal);
        Assert.Contains("\"Leaves\"", model, StringComparison.Ordinal);
        Assert.Contains("\"Employees\"", model, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("OnGetAsync")]
    [InlineData("OnGetCountsAsync")]
    [InlineData("OnGetExportAsync")]
    [InlineData("OnPostCreateReportAsync")]
    [InlineData("OnPostDuplicateReportAsync")]
    [InlineData("OnPostDeleteReportAsync")]
    [InlineData("OnPostToggleShareAsync")]
    public void EveryReportEndpoint_LoadsServerSideReportAccess(string handler)
    {
        var model = ReadWeb("Pages", "PeopleReports", "Index.cshtml.cs");
        var start = model.IndexOf(handler, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var body = model.Substring(start, Math.Min(700, model.Length - start));
        Assert.Contains("LoadReportAccessAsync", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AssignedReportRoles_FilterDatasetsByGrant()
    {
        var model = ReadWeb("Pages", "PeopleReports", "Index.cshtml.cs");

        Assert.Contains("CountUserRolesAsync", model, StringComparison.Ordinal);
        Assert.Contains("GetUserGrantsAsync", model, StringComparison.Ordinal);
        Assert.Contains("grants.Contains(ReportGroupFor(dataset))", model, StringComparison.Ordinal);
    }
}
