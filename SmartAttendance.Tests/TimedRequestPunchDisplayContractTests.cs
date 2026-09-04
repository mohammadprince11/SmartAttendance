using Xunit;

namespace SmartAttendance.Tests;

public sealed class TimedRequestPunchDisplayContractTests
{
    [Theory]
    [InlineData("Pages/Approvals/Index.cshtml.cs")]
    [InlineData("Pages/EmployeePortal/Index.cshtml.cs")]
    [InlineData("Pages/SelfServices/Index.cshtml.cs")]
    public void TimedRequestLists_ProjectCorrelatedAttendancePunches(string relativePath)
    {
        var source = ReadWeb(relativePath);

        Assert.Contains("OUTER APPLY", source, StringComparison.Ordinal);
        Assert.Contains("FROM AttendanceRecords ar", source, StringComparison.Ordinal);
        Assert.Contains("ar.EmployeeId = r.EmployeeId", source, StringComparison.Ordinal);
        Assert.Contains("ISNULL(ar.IsDeleted, 0) = 0", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE(r.FromDate, r.RequestDate, CAST(r.CreatedAt AS date))", source, StringComparison.Ordinal);
        Assert.Contains("COALESCE(r.ToDate, r.FromDate, r.RequestDate, CAST(r.CreatedAt AS date))", source, StringComparison.Ordinal);
        Assert.Contains("MIN(CASE", source, StringComparison.Ordinal);
        Assert.Contains("WHEN ar.CheckOut IS NULL OR ar.CheckOut <> ar.CheckIn", source, StringComparison.Ordinal);
        Assert.Contains("MAX(ar.CheckOut) AS ActualCheckOut", source, StringComparison.Ordinal);
        Assert.Contains("ActualCheckIn = HrmsDatabase.GetDateTime", source, StringComparison.Ordinal);
        Assert.Contains("ActualCheckOut = HrmsDatabase.GetDateTime", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Pages/Approvals/Index.cshtml")]
    [InlineData("Pages/EmployeePortal/Index.cshtml")]
    [InlineData("Pages/SelfServices/Index.cshtml")]
    public void TimedRequestLists_ShowAvailablePunchesAndAnExplicitEmptyState(string relativePath)
    {
        var source = ReadWeb(relativePath);

        Assert.Contains("ActualCheckIn.HasValue", source, StringComparison.Ordinal);
        Assert.Contains("ActualCheckOut.HasValue", source, StringComparison.Ordinal);
        Assert.Contains("!request.ActualCheckIn.HasValue && !request.ActualCheckOut.HasValue", NormalizeRowName(source), StringComparison.Ordinal);
        Assert.Contains("لا توجد بصمة", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EmployeePortal_TimeRequestPicker_ReturnsAndRendersEveryAvailablePunch()
    {
        var model = ReadWeb("Pages/EmployeePortal/Index.cshtml.cs");
        var page = ReadWeb("Pages/EmployeePortal/Index.cshtml");
        var script = ReadWeb("wwwroot/js/nxex-bottom-nav.js");

        Assert.Contains("LoadPunchDaySummariesAsync", model, StringComparison.Ordinal);
        Assert.Contains("PunchTypingEngine.Derive", model, StringComparison.Ordinal);
        Assert.Contains("checkIns = summary.Punches", model, StringComparison.Ordinal);
        Assert.Contains("checkOuts = summary.Punches", model, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", page, StringComparison.Ordinal);
        Assert.Contains("اختر تاريخ الطلب حتى تظهر بصمة الدخول أو الخروج أو كلاهما", script, StringComparison.Ordinal);
        Assert.Contains("جاري تحميل البصمات المسجلة", script, StringComparison.Ordinal);
        Assert.Contains("البصمات المسجلة ضمن تاريخ الطلب", script, StringComparison.Ordinal);
        Assert.Contains("ins.join", script, StringComparison.Ordinal);
        Assert.Contains("outs.join", script, StringComparison.Ordinal);
        Assert.Contains("cache: 'no-store'", script, StringComparison.Ordinal);
        Assert.Contains("credentials: 'same-origin'", script, StringComparison.Ordinal);
        Assert.Contains("block.hidden = false", script, StringComparison.Ordinal);
    }

    [Fact]
    public void EmployeePortal_MobileLayout_KeepsHeroCompactAndPreventsTextAutosizing()
    {
        var css = ReadWeb("wwwroot/css/zynora-employee-experience.css");

        Assert.Contains("-webkit-text-size-adjust:100%", css, StringComparison.Ordinal);
        Assert.Contains("@media(max-width:620px)", css, StringComparison.Ordinal);
        Assert.Contains(".nxex-hero h1", css, StringComparison.Ordinal);
        Assert.Contains("font-size:21px", css, StringComparison.Ordinal);
        Assert.Contains("-webkit-line-clamp:2", css, StringComparison.Ordinal);
        Assert.Contains(".nxex-identity-card", css, StringComparison.Ordinal);
        Assert.Contains("display:flex;align-items:center", css, StringComparison.Ordinal);
    }

    private static string NormalizeRowName(string source) => source.Replace("!row.ActualCheckIn", "!request.ActualCheckIn", StringComparison.Ordinal)
        .Replace("!row.ActualCheckOut", "!request.ActualCheckOut", StringComparison.Ordinal);

    private static string ReadWeb(string relativePath)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;

        var root = Assert.IsType<DirectoryInfo>(directory).FullName;
        return File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
