using System;
using System.IO;
using Xunit;

namespace SmartAttendance.Tests;

public class EmployeeUpdatesExplicitSelectionTests
{
    [Theory]
    [InlineData(null, false, 0)]
    [InlineData(1, false, 0)]
    [InlineData(500, false, 0)]
    [InlineData(null, true, 0)]
    [InlineData(-1, true, 0)]
    [InlineData(0, true, 0)]
    [InlineData(1, true, 1)]
    [InlineData(500, true, 500)]
    public void LegacyLinkOrNoChoice_IsEmpty_ExplicitPickerChoiceIsRetained(int? id, bool selected, int expected)
    {
        Assert.Equal(expected, SmartAttendance.Web.Pages.EmployeeUpdates.IndexModel.ResolveExplicitEmployeeSelection(id, selected));
    }

    private static string Read(string file)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "SmartAttendance.Web", "Pages", "EmployeeUpdates", file));
    }

    [Fact]
    public void NoSelection_DoesNotChooseFirstEmployeeOrLoadEmployeeData()
    {
        var source = Read("Index.cshtml.cs");
        var start = source.IndexOf("private async Task LoadPageAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task<List<UpdateEmployee>>", start, StringComparison.Ordinal);
        var body = source[start..end];
        Assert.DoesNotContain("Employees.FirstOrDefault", body);
        Assert.DoesNotContain("await LoadEmployeesAsync()", body);
        var guard = body.IndexOf("if (SelectedEmployeeId <= 0)", StringComparison.Ordinal);
        var earlyReturn = body.IndexOf("return;", guard, StringComparison.Ordinal);
        Assert.True(guard > 0 && earlyReturn > guard);
        Assert.True(earlyReturn < body.IndexOf("await LoadEmployeeAsync", StringComparison.Ordinal));
        Assert.True(earlyReturn < body.IndexOf("await BuildCurrentValuesAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyPage_BlanksPickerAndHidesUpdateFormsUntilSelection()
    {
        var source = Read("Index.cshtml");
        Assert.Contains("SelectedCode = Model.SelectedEmployeeId > 0 ? Model.SelectedEmployee.EmployeeNo : string.Empty", source);
        Assert.Contains("SelectedName = Model.SelectedEmployeeId > 0 ? Model.SelectedEmployee.FullName : string.Empty", source);
        Assert.Contains("@if (Model.SelectedEmployeeId <= 0)", source);
        Assert.Contains("else if (Model.Tab == \"stage\")", source);
    }

    [Fact]
    public void ExplicitSelection_IsAuthorizedBeforePageDataLoads()
    {
        var source = Read("Index.cshtml.cs");
        var start = source.IndexOf("public async Task<IActionResult> OnGetAsync", StringComparison.Ordinal);
        var end = source.IndexOf("await LoadPageAsync", start, StringComparison.Ordinal);
        Assert.Contains("CanAccessEmployeeAsync(actor, employeeId.GetValueOrDefault())", source[start..end]);
        Assert.Contains("return Forbid();", source[start..end]);
        Assert.Contains("ResolveExplicitEmployeeSelection(employeeId, employeeSelected)", source[start..end]);
    }

    [Fact]
    public void PickerAndNavigationPreserveExplicitChoice_ButBareEmployeeIdDoesNot()
    {
        var view = Read("Index.cshtml");
        Assert.Contains("name=\"employeeSelected\" value=\"true\"", view);
        foreach (var line in view.Split('\n'))
            if (line.Contains("asp-route-employeeId", StringComparison.Ordinal))
                Assert.Contains("asp-route-employeeSelected=", line);
        foreach (var line in Read("Index.cshtml.cs").Split('\n'))
            if (line.Contains("RedirectToPage(new { employeeId", StringComparison.Ordinal))
                Assert.Contains("employeeSelected = true", line);
    }
}
