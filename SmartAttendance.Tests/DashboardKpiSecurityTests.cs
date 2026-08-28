using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class DashboardKpiSecurityTests
{
    [Fact]
    public void KpiDefinitions_DeclareSourceUnitAndDrilldown()
    {
        Assert.All(DashboardWidgetStore.Metrics, metric =>
        {
            Assert.False(string.IsNullOrWhiteSpace(metric.Source));
            Assert.False(string.IsNullOrWhiteSpace(metric.Unit));
            Assert.StartsWith("/", metric.DrillPath, StringComparison.Ordinal);
        });
        Assert.Contains(DashboardWidgetStore.Metrics, metric => metric.Key == "TodayAbsent" && metric.DrillPath.Contains("Absent"));
    }

    [Fact]
    public void DashboardMutations_RequireCompanyScopeInSql()
    {
        var root = FindRoot();
        var store = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "DashboardWidgetStore.cs"));
        var page = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Index.cshtml.cs"));
        Assert.Contains("scope.Allows(companyId)", store, StringComparison.Ordinal);
        Assert.Contains("AND CompanyId=@CompanyId", store, StringComparison.Ordinal);
        Assert.Contains("ListAsync(_dbContext, scope, CompanyId.Value)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("DashboardWidgetStore.ListAsync(_dbContext);", page, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx"))) directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
