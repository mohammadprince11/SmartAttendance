using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class PageAccessRouteCatalogTests
{
    [Theory]
    [InlineData("/MissingPunchRequests", "Attendance.MissingPunch")]
    [InlineData("/LeaveRequests/Create", "People.LeaveRequests")]
    [InlineData("/Approvals", "People.Approvals")]
    [InlineData("/Payroll/Transactions", "Payroll.Transactions")]
    [InlineData("/AttendanceReports", "Attendance.Reports")]
    [InlineData("/Employees/Profile/42", "People.Profile")]
    [InlineData("/Contracts/Movements", "People.Contracts")]
    [InlineData("/Roster", "Attendance.Roster")]
    [InlineData("/Payroll/Loans", "Payroll.Loans")]
    [InlineData("/HrSettings/NotificationCenter", "HrSettings.Notifications")]
    public void LiveRoutes_MapToStablePageCodes(string path, string expected) =>
        Assert.Equal(expected, PageAccessRouteCatalog.ResolvePageCode(path));

    [Theory]
    [InlineData("GET", "/LeaveRequests", null, "View")]
    [InlineData("POST", "/LeaveRequests/Create", null, "Create")]
    [InlineData("POST", "/MissingPunchRequests", "Delete", "Delete")]
    [InlineData("POST", "/Approvals", "Approve", "Edit")]
    public void HttpOperation_MapsToCrudAction(string method, string path, string? handler, string expected) =>
        Assert.Equal(expected, PageAccessRouteCatalog.ResolveAction(method, path, handler));

    [Fact]
    public void SaveHandler_DistinguishesCreateFromEditByServerPostedId()
    {
        Assert.Equal("Create", PageAccessRouteCatalog.ResolveAction("POST", "/MissingPunchRequests", "Save", 0));
        Assert.Equal("Edit", PageAccessRouteCatalog.ResolveAction("POST", "/MissingPunchRequests", "Save", 42));
    }

    [Fact]
    public void EmployeeImportHandler_UsesDedicatedImportGrant() =>
        Assert.Equal("People.Import", PageAccessRouteCatalog.ResolvePageCode("/Employees", "Import"));

    [Fact]
    public void EveryMappedCode_ExistsInVisiblePageCatalog()
    {
        // عقد عبر السلوك العام لعينة كل موديول؛ IsValidPage يمنع مفاتيح وهمية بالواجهة.
        var codes = new[]
        {
            "People.LeaveRequests", "People.Approvals", "Attendance.MissingPunch",
            "Payroll.Transactions", "Payroll.Reports", "Identity.AccessRoles"
        };
        Assert.All(codes, code => Assert.True(PageCatalog.IsValidPage(code), code));
    }

    [Fact]
    public void EveryVisiblePageGrant_HasCentralRouteCoverage()
    {
        var visibleCodes = PageCatalog.Modules.SelectMany(module => module.Pages).Select(page => page.Code);
        Assert.Empty(visibleCodes.Except(PageAccessRouteCatalog.MappedPageCodes, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void MainNavigation_CombinesCompatibilityRoutesWithPageRoleVisibility()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var layout = File.ReadAllText(Path.Combine(
            directory!.FullName, "SmartAttendance.Web", "Pages", "Shared", "_Layout.cshtml"));

        Assert.Contains("PageAccessRouteCatalog.ResolvePageCode(path)", layout, StringComparison.Ordinal);
        Assert.Contains("CanModule(\"People\")", layout, StringComparison.Ordinal);
        Assert.Contains("CanModule(\"Attendance\")", layout, StringComparison.Ordinal);
        Assert.Contains("CanModule(\"Payroll\")", layout, StringComparison.Ordinal);
    }
}
